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
    partial class Program : MyGridProgram
    {
        //= CONFIURATION ==========================================================================

        // Whether the CIWS system is in automatic mode (shoot things in range), or
        // manual mode (use the mouse to track & fire)
        CIWSMode mode = CIWSMode.Automatic;

        // What type of weapon is the CIWS comprised of?
        CIWSType type = CIWSType.RocketLauncher;

        // Screen to use for logging
        static readonly string LOG_SCREEN = "LCD Panel - CIWS";

        // Azimuth Rotor Name
        static readonly string AZIMUTH_ROTOR_NAME = "Advanced Rotor - CIWS Azimuth";

        // Group name of gatling guns
        static readonly string CIWS_GUNS_GROUP_NAME = "CIWS GUNS";

        // Camera
        static readonly string CIWS_CAMERA_NAME = "Camera - CIWS";

        // How fast do gatling bullets travel?
        static readonly float GATLING_MUZZLE_VELOCITY = 400.0f;

        // How fast do rockets travel?
        static readonly float ROCKET_MUZZLE_VELOCITY = 200f;

        // Gyro to use for orienting the gun
        List<IMyGyro> gyros = new List<IMyGyro>();

        // A block to use for forward-facing reference
        IMyTerminalBlock forwardFacingBlock;

        // Target location
        TargetInfo target;

        // How fast the gyro turns toward the target
        double gyroGain = 60;

        // How fast the gyro slows down
        double gyroDampingGain = 0.4;

        // How far off the target should we be pointing before we start shooting (1 means exactly,
        // 0.5 means 45 degrees away
        double TARGET_ENGAGE_DOT = 0.95;

        // When should we start shooting?
        double ENGAGE_RANGE = 1200;

        // When firing missiles, what interval should each individual rocket launcher fire at?
        // (this should be equal to or greater than the missile reload time for an even fire rate)
        static readonly TimeSpan MISSILE_FIRE_RATE = TimeSpan.FromMilliseconds(1500);

        //= STATE =================================================================================

        // Gatling guns
        List<IMyUserControllableGun> gatlingGuns = new List<IMyUserControllableGun>();

        // Rotor base
        IMyMotorStator rotor;

        // Which gun we will fire from next if firing in volleys
        int gunIndex = 0;

        // Time since last volley was fired
        TimeSpan volleyTimer;

        TargetingConfig targetingConfig = new TargetingConfig();
        TargetingController controller;

        double currentTimestamp = 0;
        double prevYaw;
        double prevPitch;
        double targetDistance;
        double targetDot;
        bool firing = false;

        //= STATE =================================================================================

        enum CIWSMode
        {
            Automatic,
            Manual,
        }

        enum CIWSType
        {
            GatlingGun,
            RocketLauncher,
        }

        //= STATE =================================================================================

        public Program()
        {
            GetGroup(CIWS_GUNS_GROUP_NAME, gatlingGuns);
            GetAll(gyros);
            forwardFacingBlock = Get<IMyCameraBlock>(CIWS_CAMERA_NAME);
            rotor = Get<IMyMotorStator>(AZIMUTH_ROTOR_NAME);

            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            controller = new TargetingController(targetingConfig, null, this);
            InitLog(Me.GetSurface(0), 0.8f);

            var props = new List<ITerminalAction>();
            gatlingGuns.First().GetActions(props, x => true);
            props.ForEach(p => Echo(p.Id));
            gatlingGuns.ForEach(g => g.SetValue<bool>("Shoot", false));
        }

        //= STATE =================================================================================

        public void Main(string argument, UpdateType updateSource)
        {
            ProcessArguments(argument);

            controller.RunTargetTracking();

            switch (mode)
            {
                case CIWSMode.Manual:
                    break;

                case CIWSMode.Automatic:
                    CheckTarget();
                    OrientToTarget();
                    RunFirePolicy();
                    break;
            }

            controller.ReportStatus();

            FlushLog(Runtime.TimeSinceLastRun);
        }

        private void ProcessArguments(string argument)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                switch (argument)
                {
                    case "manual":
                        ManualMode();
                        break;
                    case "auto":
                        AutoMode();
                        break;
                    case "+gain":
                        gyroGain += 0.25;
                        break;
                    case "-gain":
                        gyroGain -= 0.25;
                        break;
                    case "+damp":
                        gyroDampingGain += 0.05;
                        break;
                    case "-damp":
                        gyroDampingGain -= 0.05;
                        break;
                }
            }
        }

        private void RunFirePolicy()
        {
            var shouldFire = false;
            do
            {
                if (target == null)
                {
                    Log.AppendFormat("Dont fire: no target\n");
                    break;
                }
                if ((currentTimestamp - targetingConfig.ChangeTargetAfter) > target.LastSeen)
                {
                    Log.AppendFormat("Don't fire: can't see target\n");
                    break;
                }
                if (targetDot < TARGET_ENGAGE_DOT)
                {
                    Log.AppendFormat("Don't fire: aligning\n");
                    break;
                }

                if (targetDistance > ENGAGE_RANGE)
                {
                    Log.AppendFormat("Don't fire: out of range\n");
                    break;
                }

                Log.AppendFormat("Fire: Target locked\n");
                shouldFire = true;
            } while (false);

            switch (type)
            {
                // Gatling gun fire policy: Turn them on if we should be firing, and off if we shouldn't.
                case CIWSType.GatlingGun:
                    if ((shouldFire && !firing) || (!shouldFire && firing))
                    {
                        firing = shouldFire;
                        gatlingGuns.ForEach(g => g.SetValue<bool>("Shoot", shouldFire));
                    }
                    break;

                // Rocket launcher policy: If we should be firing, divide our reload time by the amount of
                // launchers we have & fire them spaced out.
                case CIWSType.RocketLauncher:
                    volleyTimer += Runtime.TimeSinceLastRun;
                    var volleyInterval = TimeSpan.FromMilliseconds(MISSILE_FIRE_RATE.TotalMilliseconds / gatlingGuns.Count());
                    while (volleyTimer > volleyInterval)
                    {
                        volleyTimer -= volleyInterval;
                        gatlingGuns[gunIndex].ApplyAction("ShootOnce");
                        gunIndex = (gunIndex + 1) % gatlingGuns.Count();
                    }
                    break;
            }
        }

        private void OrientToTarget()
        {
            double pitch, yaw;
            if (target == null)
            {
                GyroTurn6(Runtime.TimeSinceLastRun.TotalMilliseconds, rotor.WorldMatrix.Backward, gyroGain, gyroDampingGain, forwardFacingBlock, gyros, prevYaw, prevPitch, out pitch, out yaw, false);
                prevYaw = yaw;
                prevPitch = pitch;
                return;
            }

            var muzzleVelocity = float.MaxValue;
            switch (type)
            {
                case CIWSType.GatlingGun:
                    muzzleVelocity = GATLING_MUZZLE_VELOCITY;
                    break;
                case CIWSType.RocketLauncher:
                    muzzleVelocity = ROCKET_MUZZLE_VELOCITY;
                    break;
            }

            // Try to aim in front of the target
            var targetPosition = GetPredictedTargetPosition2(forwardFacingBlock, Vector3D.Zero, target, muzzleVelocity);
            var targetDirection = Vector3D.Normalize(targetPosition - forwardFacingBlock.GetPosition());

            var cameraDirection = forwardFacingBlock.WorldMatrix.Forward;
            GyroTurn6(Runtime.TimeSinceLastRun.TotalMilliseconds, targetDirection, gyroGain, gyroDampingGain, forwardFacingBlock, gyros, prevYaw, prevPitch, out pitch, out yaw, false);

            prevYaw = yaw;
            prevPitch = pitch;
            targetDistance = Vector3D.Distance(targetPosition, forwardFacingBlock.GetPosition());
            targetDot = Vector3D.Dot(targetDirection, cameraDirection);

            /*
            Log.AppendFormat("Vel: {0} m\n", targetDistance);
            Log.AppendFormat("Dst: {0} m/s\n", target.Velocity.Length());
            Log.AppendFormat("Acc: {0} m/s^2\n", target.Acceleration.Length());
            Log.AppendFormat("Dot: {0}\n", Math.Round(targetDot, 5));
            Log.AppendFormat("Gyro gain: {0}/{1}\n", gyroGain, gyroDampingGain);
            */
        }

        private void CheckTarget()
        {
            if (target == null || target.LastSeen != currentTimestamp)
            {
                var closestTarget = controller.ClosestTarget();
                if (closestTarget != null)
                {
                    target = closestTarget;
                }
            }
            if (target != null && target.LastSeen < currentTimestamp - targetingConfig.ForgetTargetAfter)
            {
                target = null;
            }
        }

        private void ManualMode()
        {
            mode = CIWSMode.Manual;
            gyros.ForEach(g => g.GyroOverride = false);
            gatlingGuns.ForEach(g => g.SetValue<bool>("Shoot", false));
        }

        private void AutoMode()
        {
            mode = CIWSMode.Automatic;
        }
    }
}
