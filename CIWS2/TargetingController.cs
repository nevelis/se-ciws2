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
        class TargetingController
        {
            TargetingConfig targetingConfig;
            Program prog;
            MissileConfig[] missileConfig;
            StringBuilder Log;

            public string Status = "";

            public TargetingController(TargetingConfig targetingConfig, MissileConfig[] missileConfig, Program prog)
            {
                this.targetingConfig = targetingConfig;
                this.missileConfig = missileConfig;
                this.prog = prog;
                this.Log = prog.Log;
                if (!string.IsNullOrEmpty(targetingConfig.MissileStatusPanelName))
                {
                    prog.InitLog(prog.Get<IMyTextSurfaceProvider>(targetingConfig.MissileStatusPanelName).GetSurface(0), .8f);
                }
                else
                {
                    prog.InitLog(prog.Me.GetSurface(0));
                }

                // Find all tracking turrets
                prog.GetAll(trackingTurrets, targetingConfig.UseDesignatorsFromOtherGrids);

                // Find all merge blocks used for launching
                if (!string.IsNullOrEmpty(targetingConfig.LaunchBayTag))
                {
                    prog.GetAll(launchMergeBlocks);
                    launchMergeBlocks = launchMergeBlocks
                        .Where(x => x.DisplayNameText.Contains(targetingConfig.LaunchBayTag)).ToList();
                    launchMergeBlocks.Sort((a, b) => a.DisplayNameText.CompareTo(b.DisplayNameText));

                    prog.GetAll(launchDetachBlocks);
                    launchDetachBlocks = launchDetachBlocks
                        .Where(x => x.DisplayNameText.Contains(targetingConfig.LaunchBayTag)).ToList();
                }

                // Find the hangar doors
                if (!string.IsNullOrEmpty(targetingConfig.HangarDoorGroup))
                {
                    prog.GetGroup<IMyDoor>(targetingConfig.HangarDoorGroup, hangarDoors);
                }

                // Configure the targeting camera
                if (!string.IsNullOrEmpty(targetingConfig.TargetingCamera))
                {
                    targetingCamera = prog.Get<IMyCameraBlock>(targetingConfig.TargetingCamera);
                    targetingCamera.EnableRaycast = true;
                }

                // Missile config setup
                if (missileConfig != null)
                {
                    defaultMovingTargetMissile = missileConfig.Where(c => c.Name == targetingConfig.DefaultMovingTargetMissile).First();
                    defaultStaticTargetMissile = missileConfig.Where(c => c.Name == targetingConfig.DefaultStaticTargetMissile).First();
                }
                Status = "Ready";
            }

            public void RunMain()
            {
                RunTargetTracking();
                RunLaunchSequence();
                RunGuidance();
                RunMissileCleanup();
                ReportStatus();
            }

            public void RunTargetTracking()
            {
                // Loop through turrets
                bool updatedTimestamp = false;
                foreach (var turret in trackingTurrets)
                {
                    // Find interesting things & record their positions.
                    var entity = turret.GetTargetedEntity();
                    if (entity.IsEmpty())
                    {
                        continue;
                    }

                    // The turret is looking at something interesting.  Do we know about it already?
                    TargetInfo info = null;
                    if (!trackingTargets.TryGetValue(entity.EntityId, out info))
                    {
                        // ... no, create a new entry for it.
                        info = trackingTargets[entity.EntityId] = new TargetInfo(entity.EntityId, TargetType.Tracked, entity.Relationship);
                    }

                    info.Track(entity.Position, entity.Velocity, entity.TimeStamp);

                    // We have the current timestamp.
                    updatedTimestamp = true;
                    prog.currentTimestamp = entity.TimeStamp;
                }

                // If we haven't updated the timestamp, simulate it.  The timestamp is not available to
                // us directly; we must obtain it from a turret.
                if (!updatedTimestamp)
                {
                    prog.currentTimestamp += prog.Runtime.TimeSinceLastRun.TotalMilliseconds;
                }

                foreach (var item in trackingTargets)
                {
                    var t = item.Value;
                    if (t.TargetType != TargetType.Static && t.LastSeen < prog.currentTimestamp - targetingConfig.ForgetTargetAfter)
                    {
                        toRemove.Add(item.Key);
                        continue;
                    }

                    if (t.LastSeen != prog.currentTimestamp)
                    {
                        t.Predict(prog.currentTimestamp);
                    }
                }

                foreach (var id in toRemove)
                {
                    trackingTargets.Remove(id);
                }
                toRemove.Clear();
            }

            HashSet<long> toRemove = new HashSet<long>();

            public void RunMissileCleanup()
            {
                for (int i = 0; i < ActiveMissiles.Count(); ++i)
                {
                    var m = ActiveMissiles[i];
                    if (m.state == MissileState.Defunct)
                    {
                        if (ActiveMissiles.Any())
                        {
                            ActiveMissiles[i] = ActiveMissiles.Last();
                        }
                        ActiveMissiles.RemoveAt(ActiveMissiles.Count() - 1);
                    }
                }
            }
            public void Scuttle()
            {
                PendingMissiles.Clear();
                foreach (var m in ActiveMissiles)
                {
                    m.Detonate(true);
                }
            }

            public void ExemptScannedObjectFromTargeting()
            {
                var entity = targetingCamera.Raycast(5000);
                if (!entity.IsEmpty())
                {
                    Status = "Exempting " + entity.EntityId + " from targeting";
                    exemptTargets.Add(entity.EntityId);
                }
            }
            public void ClearExemptTargets()
            {

                Status = "Cleared exempt targets";
                exemptTargets.Clear();
            }

            public TargetInfo ClosestTarget()
            {
                TargetInfo closestTarget = null;
                double closestDistance = 0;

                foreach (var item in trackingTargets)
                {
                    var target = item.Value;
                    if (target.LastSeen == prog.currentTimestamp && !exemptTargets.Contains(target.EntityId))
                    {
                        var distance = Vector3D.Distance(target.Position, prog.Me.CubeGrid.WorldVolume.Center);
                        if (closestTarget == null || distance < closestDistance)
                        {
                            closestTarget = target;
                            closestDistance = distance;
                        }
                    }
                }
                return closestTarget;
            }

            public TargetInfo FarthestTarget()
            {
                TargetInfo farthestTarget = null;
                double farthestDistance = 0;

                foreach (var item in trackingTargets)
                {
                    var target = item.Value;
                    if (target.LastSeen == prog.currentTimestamp && !exemptTargets.Contains(target.EntityId))
                    {
                        var distance = Vector3D.Distance(target.Position, prog.Me.CubeGrid.WorldVolume.Center);
                        if (farthestTarget == null || distance > farthestDistance)
                        {
                            farthestTarget = target;
                            farthestDistance = distance;
                        }
                    }
                }
                return farthestTarget;
            }

            public TargetInfo MostThreateningTarget()
            {
                TargetInfo mostThreateningTarget = null;
                double highestDot = -1;

                foreach (var item in trackingTargets)
                {
                    var target = item.Value;
                    if (target.LastSeen == prog.currentTimestamp && !exemptTargets.Contains(target.EntityId))
                    {
                        var incomingLOS = Vector3D.Normalize(-(target.Position - prog.Me.CubeGrid.WorldVolume.Center));
                        var incomingDirection = Vector3D.Normalize(target.Velocity);
                        var dot = Vector3D.Dot(incomingLOS, incomingDirection);
                        if (dot > highestDot)
                        {

                        }
                        var distance = Vector3D.Distance(target.Position, prog.Me.CubeGrid.WorldVolume.Center);
                        if (mostThreateningTarget == null || dot > highestDot)
                        {
                            mostThreateningTarget = target;
                            highestDot = dot;
                        }
                    }
                }
                return mostThreateningTarget;
            }

            public TargetInfo FindTarget(long entityId)
            {
                return trackingTargets.Where(t => t.Key == entityId).Select(t => t.Value).FirstOrDefault();
            }

            public void FireMissileAtClosestTarget(string missileType = "")
            {
                var closestTarget = ClosestTarget();
                LaunchMissile(closestTarget, missileType);
            }

            public void FireMissileAtAllTargets(string missileType = "")
            {
                foreach (var item in trackingTargets)
                {
                    var target = item.Value;
                    if (target.LastSeen == prog.currentTimestamp && !exemptTargets.Contains(target.EntityId))
                    {
                        LaunchMissile(target, missileType);
                    }
                }
            }

            Random rnd = new Random();
            private long GenerateRandomId()
            {
                return (long)rnd.Next() << 32 | (long)rnd.Next();
            }

            public void LaunchMissileAtStaticTarget(Vector3D targetPosition, string missileType = "")
            {
                TargetInfo target = new TargetInfo(GenerateRandomId(), TargetType.Static, MyRelationsBetweenPlayerAndBlock.Neutral); ;
                target.Track(targetPosition, Vector3D.Zero, prog.currentTimestamp);
                LaunchMissile(target, missileType);
            }

            public void LaunchCustomGuidedMissile(TargetInfo target)
            {
                LaunchMissile(target, targetingConfig.DefaultMovingTargetMissile);
            }

            private void LaunchMissile(TargetInfo target, string missileType)
            {
                if (target == null)
                {
                    Status = "No visible targets";
                    return;
                }

                var config = target.TargetType == TargetType.Static ? defaultStaticTargetMissile : defaultMovingTargetMissile;
                if (!string.IsNullOrEmpty(missileType))
                {
                    config = missileConfig.Where(c => c.Name == missileType).FirstOrDefault();
                }

                if (config == null)
                {
                    Status = "Invalid fire argument. Usage:\n fire|fireAll [" +
                        string.Join("|", missileConfig.Select(c => c.Name)) + ']';
                    return;
                }

                Missile missile = AllocateNewMissile(config);
                if (missile == null)
                {
                    return;
                }

                missile.target = target;
                PendingMissiles.Add(missile);
            }

            List<IMyTerminalBlock> partsList = new List<IMyTerminalBlock>();

            private List<IMyTerminalBlock> PartsOfType<T>(List<IMyTerminalBlock> parts, double distance = 0, IMyEntity from = null) where T : IMyTerminalBlock
            {
                return parts
                    .Where(
                        x => x is T &&
                        (
                            distance == 0 ||
                            distance > Vector3D.Distance(
                                x.GetPosition(),
                                from == null ? prog.Me.GetPosition() : from.GetPosition()
                            )
                        )
                    )
                    .ToList();
            }

            private Missile AllocateNewMissile(MissileConfig config)
            {
                StringBuilder sb = new StringBuilder();

                // Find all of the terminal blocks containing the missile tag.  Calls to the GTS are
                // expensive, so do this once per missile allocation.
                partsList.Clear();
                prog.GridTerminalSystem.GetBlocksOfType(partsList, b => b.DisplayNameText.Contains('[' + config.Name + ']'));

                var missileMergeBlocks = PartsOfType<IMyShipMergeBlock>(partsList);

                Missile missile = null;

                var awaitingLaunch = PendingMissiles.Select(x => x.missileMergeBlock).Where(x => x != null).ToDictionary(x => x.EntityId, x => true);
                // Find a detach block with a missile on it
                foreach (var dockDetachBlock in launchDetachBlocks)
                {
                    if (!dockDetachBlock.IsAttached)
                    {
                        continue;
                    }

                    var missileParts = partsList.Where(p => p.CubeGrid.EntityId == dockDetachBlock.TopGrid.EntityId).ToList();
                    try
                    {
                        var m = new Missile
                        {
                            config = config.Clone(),
                            dockingBlock = dockDetachBlock,
                        };

                        m.remotes = PartsOfType<IMyRemoteControl>(missileParts, config.RemoteDistance, dockDetachBlock).Select(x => x as IMyRemoteControl).ToList();
                        m.connectors = PartsOfType<IMyShipConnector>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyShipConnector).ToList();
                        m.gyros = PartsOfType<IMyGyro>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyGyro).ToList();
                        m.warheads = PartsOfType<IMyWarhead>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyWarhead).ToList();
                        m.batteries = PartsOfType<IMyBatteryBlock>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyBatteryBlock).ToList();
                        m.thrusters = PartsOfType<IMyThrust>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyThrust).ToList();
                        m.fuelTanks = PartsOfType<IMyGasTank>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMyGasTank).ToList();
                        m.sensor = PartsOfType<IMySensorBlock>(missileParts, config.MaxComponentDistance, dockDetachBlock).Select(x => x as IMySensorBlock).FirstOrDefault();

                        if (!m.IsReady)
                        {
                            /*
                            sb.AppendLine("Incomplete missile on bay " + dockDetachBlock.DisplayNameText);
                            sb.AppendLine("Gyros: " + m.gyros.Count());
                            sb.AppendLine("Remotes: " + m.remotes.Count());
                            sb.AppendLine("Connectors: " + m.connectors.Count());
                            sb.AppendLine("Merge: " + m.missileMergeBlock);
                            sb.AppendLine("warheads: " + m.warheads.Count());
                            sb.AppendLine("fueltanks: " + m.fuelTanks.Count());
                            sb.AppendLine("batteries: " + m.batteries.Count());
                            sb.AppendLine("thrusters: " + m.thrusters.Count());
                            */
                            continue;
                        }

                        sb.AppendLine("Missile allocated from bay " + dockDetachBlock.DisplayNameText);
                        Status = sb.ToString();
                        return m;
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine(string.Format("Error with missile on launch bay {0}: {1}", dockDetachBlock.DisplayNameText, ex.Message));
                    }
                }

                // Find a merge block with a missile on it
                foreach (var dockMergeBlock in launchMergeBlocks)
                {
                    if (!dockMergeBlock.IsConnected)
                    {
                        // sb.AppendLine("Launch block " + dockMergeBlock.DisplayNameText + " is not attached");
                        continue;
                    }

                    var missileMergeBlock = missileMergeBlocks.Where(m => Vector3D.Distance(m.GetPosition(), dockMergeBlock.GetPosition()) < 5).FirstOrDefault() as IMyShipMergeBlock;
                    if (missileMergeBlock == null)
                    {
                        // sb.AppendLine("Wrong missile type on launch block " + dockMergeBlock.DisplayNameText);
                        continue;
                    }

                    if (awaitingLaunch.ContainsKey(missileMergeBlock.EntityId))
                    {
                        // sb.AppendLine("Launch block " + dockMergeBlock.DisplayNameText + " is already scheduled for launch");
                        continue;
                    }

                    try
                    {
                        var m = new Missile
                        {
                            config = config.Clone(),
                            dockingBlock = dockMergeBlock,
                            missileMergeBlock = missileMergeBlock
                        };

                        m.remotes = PartsOfType<IMyRemoteControl>(partsList, config.RemoteDistance, missileMergeBlock).Select(x => x as IMyRemoteControl).ToList();
                        m.connectors = PartsOfType<IMyShipConnector>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyShipConnector).ToList();
                        m.gyros = PartsOfType<IMyGyro>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyGyro).ToList();
                        m.warheads = PartsOfType<IMyWarhead>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyWarhead).ToList();
                        m.batteries = PartsOfType<IMyBatteryBlock>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyBatteryBlock).ToList();
                        m.thrusters = PartsOfType<IMyThrust>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyThrust).ToList();
                        m.fuelTanks = PartsOfType<IMyGasTank>(partsList, config.MaxComponentDistance, missileMergeBlock).Select(x => x as IMyGasTank).ToList();

                        if (!m.IsReady)
                        {
                            /*
                            sb.AppendLine("Incomplete missile on bay " + dockMergeBlock.DisplayNameText);
                            sb.AppendLine("Gyros: " + m.gyros.Count());
                            sb.AppendLine("Remotes: " + m.remotes.Count());
                            sb.AppendLine("Connectors: " + m.connectors.Count());
                            sb.AppendLine("Merge: " + m.missileMergeBlock);
                            sb.AppendLine("warheads: " + m.warheads.Count());
                            sb.AppendLine("fueltanks: " + m.fuelTanks.Count());
                            sb.AppendLine("batteries: " + m.batteries.Count());
                            sb.AppendLine("thrusters: " + m.thrusters.Count());
                            */
                            continue;
                        }

                        sb.AppendLine("Missile allocated from bay " + dockMergeBlock.DisplayNameText);
                        missile = m;
                        break;
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine(string.Format("Error with missile on launch bay {0}: {1}", dockMergeBlock.DisplayNameText, ex.Message));
                    }
                }

                if (missile == null)
                {
                    sb.AppendLine("No missiles available!");
                }
                Status = sb.ToString();
                return missile;
            }

            public void RunGuidance()
            {
                foreach (var missile in ActiveMissiles)
                {
                    switch (missile.state)
                    {
                        case MissileState.Idle:
                            if (missile.missileMergeBlock != null)
                            {
                                missile.missileMergeBlock.Enabled = false;
                            }
                            else if (missile.dockingBlock is IMyMechanicalConnectionBlock)
                            {
                                (missile.dockingBlock as IMyMechanicalConnectionBlock).Detach();
                            }
                            missile.state = MissileState.Detaching;
                            break;
                        case MissileState.Detaching:
                            if (missile.IsDetached)
                            {
                                missile.connectors.ForEach(c => {
                                    c.Disconnect();
                                });

                                // Prepare the missile for launch
                                missile.Initialize();
                                missile.state = MissileState.Initializing;
                            }
                            break;
                        case MissileState.Initializing:
                            if (missile.remotes.First().CalculateShipMass().PhysicalMass > 0)
                            {
                                // Go go go
                                missile.PowerOn();
                                missile.state = MissileState.Launching;
                            }
                            break;
                        case MissileState.Launching:
                            if (missile.IsClear)
                            {
                                missile.state = MissileState.Tracking;
                                if (missile.sensor != null)
                                {
                                    missile.sensor.Enabled = true;
                                }
                            }
                            break;
                        case MissileState.Tracking:
                            if (missile.target.TargetType == TargetType.Tracked)
                            {
                                if (TimeSpan.FromMilliseconds(prog.currentTimestamp - missile.target.LastSeen).TotalMilliseconds > targetingConfig.ChangeTargetAfter)
                                {
                                    var newTarget = ClosestTarget();
                                    if (newTarget != null)
                                    {
                                        missile.target = newTarget;
                                    }
                                }
                            }
                            missile.RunGuidance(prog.Runtime.TimeSinceLastRun.TotalMilliseconds);
                            break;
                        case MissileState.Defunct:
                            break;
                    }

                    switch (missile.state)
                    {
                        case MissileState.Launching:
                        case MissileState.Tracking:
                            if (missile.IsDefunct)
                            {
                                missile.Detonate();
                                missile.state = MissileState.Defunct;
                            }
                            break;
                    }
                }
            }

            TimeSpan timeSinceLastLaunch = TimeSpan.Zero;

            public void RunLaunchSequence()
            {
                timeSinceLastLaunch = timeSinceLastLaunch.Add(prog.Runtime.TimeSinceLastRun);

                if (PendingMissiles.Count() == 0)
                {
                    // Nothing to shoot
                    return;
                }

                if (hangarDoors.Any(d => d.Status != DoorStatus.Open))
                {
                    // The doors are closed
                    return;
                }

                if (timeSinceLastLaunch > TimeSpan.FromMilliseconds(targetingConfig.LaunchDelay))
                {
                    // Fire one missile
                    timeSinceLastLaunch = TimeSpan.Zero;
                    ActiveMissiles.Add(PendingMissiles.First());
                    PendingMissiles.RemoveAt(0);
                }
            }
            public void ReportStatus()
            {
                Log.AppendFormat("Current Timestamp: {0:0.0}\n", prog.currentTimestamp)
                    .AppendFormat("Launch bays: {0}\n", launchMergeBlocks.Count() + launchDetachBlocks.Count())
                    .AppendFormat("Trackers: {0}\n", trackingTurrets.Count())
                    .AppendFormat("Missiles: {0} active, {1} pending\n", ActiveMissiles.Count(), PendingMissiles.Count())
                    .AppendFormat("Targets: {0} ({1} exempt)\n", trackingTargets.Count(), exemptTargets.Count())
                    .AppendLine(Status);

                ReportMissiles();
                // ReportTargets();
            }

            private void ReportMissiles()
            {
                foreach (var m in ActiveMissiles)
                {
                    var reference = m.gyros.First().CubeGrid;
                    Log.AppendFormat("{0}: {1}\n", reference.EntityId, Vector3D.Distance(m.target.Position, reference.GetPosition()));
                    Log.AppendFormat("CruiseDot: {0}\n", m.CruiseDot);
                    /*
                    Log.AppendLine("Mass: " + m.remotes.First().CalculateShipMass().PhysicalMass);
                    Log.AppendLine("missileAcceleration: " + m.missileAcceleration);
                    Log.AppendFormat("LateralDirection: {0}\n", Vector3D.Round(m.LateralDirection, 2));
                    Log.AppendFormat("LateralAccelerationComponent: {0}\n", Vector3D.Round(m.LateralAccelerationComponent, 2));
                    Log.AppendFormat("Direction: {0}\n", Vector3D.Round(m.directionVector, 2));
                    Log.AppendFormat("Velocity: {0}\n", Vector3D.Round(m.previousTargetVelocity, 2));
                    Log.AppendFormat("Yaw: {0} Pitch: {1}\n", m.prevYaw, m.prevPitch);
                    Log.AppendLine("State: " + m.state);
                    Log.AppendFormat("oversteer: {0} badThrust: {1} badRejectedAccel: {2}\n", m.oversteer, m.badThrust, m.badRejectedAccel);
                    */
                }
            }

            private void ReportTargets()
            {
                // Report the interesting things we are looking at.
                foreach (var item in trackingTargets)
                {
                    var target = item.Value;
                    Log.AppendLine("Target: " + target.Relationship + " (" + target.EntityId + ")");
                    Log.AppendLine("Distance: " + Vector3D.Distance(target.Position, prog.Me.GetPosition()));

                    Log.Append("Last Seen: ");
                    if (target.LastSeen == prog.currentTimestamp)
                    {
                        Log.AppendLine("now");
                    }
                    else
                    {
                        Log.AppendLine(TimeSpan.FromMilliseconds(prog.currentTimestamp - target.LastSeen).TotalSeconds + " ago");
                    }
                }
            }

            MissileConfig defaultMovingTargetMissile;
            MissileConfig defaultStaticTargetMissile;

            // Camera used for scanning targets
            IMyCameraBlock targetingCamera;

            // List of turrets that we are tracking from
            List<IMyLargeTurretBase> trackingTurrets = new List<IMyLargeTurretBase>();

            // A collection of targets that we know about
            Dictionary<long, TargetInfo> trackingTargets = new Dictionary<long, TargetInfo>();

            // A list of merge blocks that we can launch missiles off
            List<IMyShipMergeBlock> launchMergeBlocks = new List<IMyShipMergeBlock>();

            // A list of rotors we can detach to launch missiles
            List<IMyMechanicalConnectionBlock> launchDetachBlocks = new List<IMyMechanicalConnectionBlock>();

            // Hangar doors
            List<IMyDoor> hangarDoors = new List<IMyDoor>();

            // A list of active missiles
            public List<Missile> ActiveMissiles = new List<Missile>();

            // A list of missiles ready to fire
            public List<Missile> PendingMissiles = new List<Missile>();

            // Things we shouldn't shoot
            HashSet<long> exemptTargets = new HashSet<long>();

            public int LaunchBayCount { get { return launchMergeBlocks.Count(); } }
        }
    }
}
