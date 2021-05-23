using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;


namespace IngameScript
{
    partial class Program
    {
        enum MissileState
        {
            Idle,
            Detaching,
            Initializing,
            Launching,
            Tracking,
            Defunct,
        }

        // A class that describes a missile, including all of its functional parts required for
        // guidance and flight.
        class Missile
        {
            // Missile configuration
            public MissileConfig config;

            // Sensor block
            public IMySensorBlock sensor;

            // Remote Control: provides gravitational component
            public List<IMyRemoteControl> remotes = new List<IMyRemoteControl>();

            // Gyroscope: provides directional control
            public List<IMyGyro> gyros;

            // Thrusters: provides... thrust :)
            public List<IMyThrust> thrusters = new List<IMyThrust>();

            // Batteries: should be set to recharge, and will be set to auto upon launch
            public List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();

            // Fuel tanks: should have stockpile on, and will set stockpile off upon launch
            public List<IMyGasTank> fuelTanks = new List<IMyGasTank>();

            // Warheads: should be disarmed until close enough to the target (and far enough away
            // from us!)
            public List<IMyWarhead> warheads = new List<IMyWarhead>();

            // Merge block: used to launch missile
            public IMyShipMergeBlock missileMergeBlock;

            // Merge block or Rotor/Hinge base: what the missile was launched from
            public IMyTerminalBlock dockingBlock;

            // Connectors: optional, used for fueling
            public List<IMyShipConnector> connectors = new List<IMyShipConnector>();

            // What are we flying at?
            public TargetInfo target;

            // Current missile state
            public MissileState state = MissileState.Idle;

            // Should we spin round?
            public bool Spinning = false;

            public double missileMass;
            public double missileAcceleration;
            public double missileThrust;
            public double CruiseOffset { get; private set; }

            // For guidance
            public Vector3D previousMissilePosition { get; private set; }
            public Vector3D previousTargetPosition { get; private set; }
            public Vector3D previousTargetVelocity { get; private set; }
            public Vector3D directionVector { get; private set; }
            public Vector3D LateralDirection { get; private set; }
            public Vector3D LateralAccelerationComponent { get; private set; }
            public double CruiseDot { get; private set; }

            public double prevYaw = 0;
            public double prevPitch = 0;
            public double remainingDistance = 0;
            public double lastRemainingDistance = 0;
            public double linearVelocity = 0;
            public double eta = 0;

            public bool oversteer { get; private set; }
            public bool badThrust { get; private set; }
            public bool badRejectedAccel { get; private set; }

            public Vector3D Position
            {
                get
                {
                    return batteries.First().CubeGrid.WorldVolume.Center;
                }
            }

            public bool IsReady
            {
                get
                {
                    // It is okay if we have more than required because we will remove those blocks
                    // after we have detached.
                    return config != null &&
                        (missileMergeBlock != null || dockingBlock is IMyMechanicalConnectionBlock) &&
                        remotes.Any() &&
                        gyros.Count() >= config.GyroCount &&
                        warheads.Count() >= config.WarheadCount &&
                        thrusters.Count() >= config.ThrusterCount &&
                        batteries.Count() >= config.BatteryCount &&
                        fuelTanks.Count() >= config.FuelTankCount &&
                        (config.SensorCount == 0 || sensor != null);
                }
            }

            public bool IsDetached
            {
                get
                {
                    if (missileMergeBlock != null)
                    {
                        return !missileMergeBlock.IsSameConstructAs(dockingBlock);
                    }
                    return !(dockingBlock as IMyMechanicalConnectionBlock).IsAttached;
                }
            }

            public bool IsClear
            {
                get
                {
                    var referenceBlock = missileMergeBlock == null ? batteries.First() : missileMergeBlock as IMyTerminalBlock;
                    return Vector3D.Distance(referenceBlock.GetPosition(), dockingBlock.GetPosition()) > config.LaunchDistance;
                }
            }

            public bool IsDefunct
            {
                get
                {
                    return gyros.Any(g => g.CubeGrid.GetCubeBlock(g.Position) == null) ||
                         remotes.First().CubeGrid.GetCubeBlock(remotes.First().Position) == null ||
                         thrusters.Any(t => t.CubeGrid.GetCubeBlock(t.Position) == null) ||
                         (fuelTanks.Any() && fuelTanks.All(t => t.FilledRatio == 0)) ||
                         batteries.All(b => !b.HasCapacityRemaining);
                }
            }

            public void Detonate(bool forceArm = false)
            {
                warheads.ForEach(w => {
                    if (forceArm)
                    {
                        w.IsArmed = true;
                    }
                    w.Detonate();
                });
            }

            public void RecalculatePhysics()
            {
                missileThrust = thrusters.Sum(t => t.MaxThrust);
                missileMass = remotes.First().CalculateShipMass().PhysicalMass;
                missileAcceleration = missileThrust / missileMass;
            }

            public void PowerOn()
            {
                // Now that we are initialized, calculate the mass & acceleration
                RecalculatePhysics();

                gyros.ForEach(g => {
                    g.Enabled = true;
                });
                batteries.ForEach(b => b.ChargeMode = ChargeMode.Discharge);
                fuelTanks.ForEach(t => t.Stockpile = false);
                thrusters.ForEach(t => {
                    t.Enabled = true;
                    t.ThrustOverridePercentage = config.LaunchThrust;
                });

                // Calculate the offset from the center of the planet that the missile should
                // cruise at, if the target is around the curvature of the planet
                var remote = remotes.First();
                Vector3D planetPosition;
                if (remote.TryGetPlanetPosition(out planetPosition))
                {
                    CruiseOffset = Vector3D.Distance(planetPosition, remote.GetPosition()) + config.CruiseAltitude;
                }
            }

            public void RunGuidance(double milliseconds)
            {
                var ticks = milliseconds / 1000.0;

                //Sorts CurrentVelocities
                Vector3D MissilePositionPrev = previousMissilePosition;
                Vector3D MissileVelocity = (Position - MissilePositionPrev) / ticks;

                Vector3D TargetPosition = target.Position;

                // Try to go around planets if the target is on the other side
                var remote = remotes.First();
                var gravity = remote.GetNaturalGravity();

                Vector3D planetCenter;

                // Calculate the cruise dot
                if (remote.TryGetPlanetPosition(out planetCenter))
                {
                    var missilePlanetDir = Vector3D.Normalize(Position - planetCenter);
                    var targetPlanetDir = Vector3D.Normalize(TargetPosition - planetCenter);

                    CruiseDot = Vector3D.Dot(missilePlanetDir, targetPlanetDir);
                    if (CruiseDot < 0.995)
                    {
                        // We should be cruising because we are too far away from the target

                        var missileDirection = Vector3D.Normalize(TargetPosition - Position);

                        // As we are getting further away from the ground & approaching cruise
                        // altitude, tip the missile forward
                        var desiredDirection = -Vector3D.Normalize(gravity);
                        var distanceAboveLaunch = Vector3D.Distance(Position, planetCenter);

                        var distanceAboveCruise = Math.Max(distanceAboveLaunch - config.CruiseAltitude, 0);
                        var pitchDownRatio = (Math.Min(distanceAboveLaunch, config.CruiseAltitude) + distanceAboveLaunch / 10) / config.CruiseAltitude;

                        var axis = Vector3D.Cross(missileDirection, desiredDirection);
                        var rotation = Matrix.CreateFromAxisAngle(axis, (float)(pitchDownRatio * Math.PI / 2));
                        desiredDirection = Vector3D.Rotate(desiredDirection, rotation);

                        var coords = remote.GetPosition() + desiredDirection * 100.0;
                        TargetPosition = coords;
                    }
                }

                Vector3D TargetPositionPrev = previousTargetPosition;
                Vector3D TargetVelocity = (TargetPosition - previousTargetPosition) / ticks;

                //Uses RdavNav Navigation APN Guidance System
                //-----------------------------------------------

                //Setup LOS rates and PN system
                Vector3D LOS_Old = Vector3D.Normalize(TargetPositionPrev - MissilePositionPrev);
                Vector3D LOS_New = Vector3D.Normalize(TargetPosition - Position);
                Vector3D Rel_Vel = Vector3D.Normalize(TargetVelocity - MissileVelocity);

                //And Assigners
                double LOS_Rate;
                Vector3D LOS_Delta;
                Vector3D MissileForwards = thrusters.First().WorldMatrix.Backward;

                //Vector/Rotation Rates
                if (LOS_Old.Length() == 0)
                {
                    LOS_Rate = 0.0;
                    LOS_Delta = Vector3D.Zero;
                }
                else
                {
                    LOS_Delta = LOS_New - LOS_Old;
                    LOS_Rate = LOS_Delta.Length() / ticks;
                }

                //-----------------------------------------------

                //Closing Velocity
                double Vclosing = (TargetVelocity - MissileVelocity).Length();
                Vector3D GravityComp = -gravity;

                //Calculate the final lateral acceleration
                LateralDirection = Vector3D.Normalize(Vector3D.Cross(Vector3D.Cross(Rel_Vel, LOS_New), Rel_Vel));
                LateralAccelerationComponent = LateralDirection * config.PNGain * LOS_Rate * Vclosing + LOS_Delta * 9.8 * (0.5 * config.PNGain);

                //If Impossible Solution (ie maxes turn rate) Use Drift Cancelling For Minimum T
                double OversteerReqt = (LateralAccelerationComponent).Length() / missileAcceleration;
                if (OversteerReqt > 0.98)
                {
                    LateralAccelerationComponent = missileAcceleration * Vector3D.Normalize(LateralAccelerationComponent + (OversteerReqt * Vector3D.Normalize(-MissileVelocity)) * 40);
                    oversteer = true;
                }
                else
                {
                    oversteer = false;
                }

                //Calculates And Applies Thrust In Correct Direction (Performs own inequality check)
                double ThrustPower = Vector3D.Dot(MissileForwards, Vector3D.Normalize(LateralAccelerationComponent));
                if (ThrustPower == double.NaN)
                {
                    ThrustPower = 0;
                    badThrust = true;
                }
                else
                {
                    badThrust = false;
                }


                ThrustPower = MathHelper.Clamp(ThrustPower, 0.1, 1);
                foreach (var t in thrusters.Where(t => t.ThrustOverride != (t.MaxThrust * ThrustPower)))
                {
                    t.ThrustOverride = (float)(t.MaxThrust * ThrustPower);
                }

                //Calculates Remaining Force Component And Adds Along LOS
                double RejectedAccel = Math.Sqrt(missileAcceleration * missileAcceleration - LateralAccelerationComponent.LengthSquared());
                if (double.IsNaN(RejectedAccel))
                {
                    RejectedAccel = 0;
                    badRejectedAccel = true;
                }
                else
                {
                    badRejectedAccel = false;
                }
                LateralAccelerationComponent = LateralAccelerationComponent + LOS_New * RejectedAccel;

                //-----------------------------------------------

                //Guides To Target Using Gyros
                directionVector = Vector3D.Normalize(LateralAccelerationComponent + GravityComp);
                double Yaw;
                double Pitch;
                GyroTurn6(milliseconds, directionVector, config.GyroGain, config.GyroDampingGain, remotes.First(),
                    gyros, prevYaw, prevPitch, out Pitch, out Yaw, Spinning);

                // Updates For Next Tick Round
                previousTargetPosition = TargetPosition;
                previousTargetVelocity = TargetVelocity;
                previousMissilePosition = Position;
                prevYaw = Yaw;
                prevPitch = Pitch;
                lastRemainingDistance = remainingDistance;
                remainingDistance = (TargetPosition - Position).Length();
                linearVelocity = MissileVelocity.Length();
                eta = remainingDistance / linearVelocity;

                if (warheads.Count() > 0)
                {
                    if (remainingDistance < config.ArmDistance)
                    {
                        warheads.ForEach(w => w.IsArmed = true);
                    }

                    var warheadDistance = Vector3D.Distance(warheads.First().GetPosition(), TargetPosition);
                    if (warheadDistance < config.DetonateDistance)
                    {
                        Detonate();
                    }

                    if (sensor != null && !sensor.LastDetectedEntity.IsEmpty())
                    {
                        Detonate();
                    }
                }
            }

            public void Initialize()
            {
                if (missileMergeBlock == null)
                {
                    // We don't need to remove anything because we were already on a different grid
                    return;
                }

                // Remove thrusters & nukes we have picked up by mistake
                remotes = remotes.Where(g => g.IsSameConstructAs(missileMergeBlock)).ToList();
                gyros = gyros.Where(g => g.IsSameConstructAs(missileMergeBlock)).ToList();
                thrusters = thrusters.Where(t => t.IsSameConstructAs(missileMergeBlock)).ToList();
                warheads = warheads.Where(t => t.IsSameConstructAs(missileMergeBlock)).ToList();
                batteries = batteries.Where(t => t.IsSameConstructAs(missileMergeBlock)).ToList();
                fuelTanks = fuelTanks.Where(t => t.IsSameConstructAs(missileMergeBlock)).ToList();
            }

            public string Diagnose()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("remote distance: " + Vector3D.Distance(remotes.First().GetPosition(), missileMergeBlock.GetPosition()));
                sb.AppendLine("Gyro distance: " + gyros.Max(w => Vector3D.Distance(w.GetPosition(), missileMergeBlock.GetPosition())));
                sb.AppendLine("warheads distance: " + warheads.Max(w => Vector3D.Distance(w.GetPosition(), missileMergeBlock.GetPosition())));
                sb.AppendLine("batteries distance: " + batteries.Max(w => Vector3D.Distance(w.GetPosition(), missileMergeBlock.GetPosition())));
                sb.AppendLine("thrusters distance: " + thrusters.Max(w => Vector3D.Distance(w.GetPosition(), missileMergeBlock.GetPosition())));
                sb.AppendLine("fuelTanks distance: " + fuelTanks.Max(w => Vector3D.Distance(w.GetPosition(), missileMergeBlock.GetPosition())));
                return sb.ToString();
            }
        }
    }
}
