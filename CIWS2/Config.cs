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

        class TargetingConfig
        {
            /// How long should we track targets we can't see before forgetting them
            public uint ForgetTargetAfter;

            /// If there are other available targets, change after this long
            public uint ChangeTargetAfter;

            /// Minimum delay between missile launches
            public uint LaunchDelay;

            /// The tag that indicates a merge block may be used to launch missiles from
            public string LaunchBayTag;

            /// The name of the group of hangar doors to ensure are open before firing anything
            public string HangarDoorGroup;

            /// Name of camera used for target acquisition
            public string TargetingCamera;

            /// What do we shoot by default at moving targets if no missile type is specified
            public string DefaultMovingTargetMissile;

            /// What do we shoot by default at static targets
            public string DefaultStaticTargetMissile;

            /// Display missile & targeting information on this screen
            public string MissileStatusPanelName;

            /// If we should use turrets from other grids (false means just this grid)
            public bool UseDesignatorsFromOtherGrids;
        }

        class MissileConfig
        {
            /// Name of missile configuration.  We expect to find "[Name]" on each functional block
            /// that is part of the missile.
            public string Name;

            /// How many thrusters the missile should have
            public uint ThrusterCount;

            /// How many batteries the missile should have
            public uint BatteryCount;

            /// How many fuel tanks the missile should have
            public uint FuelTankCount;

            /// How many warheads the missile should have
            public uint WarheadCount;

            /// How many gyroscopes the missile should have
            public uint GyroCount = 1;

            /// How many sensors the missile should have
            public uint SensorCount;


            /// How far from the merge block should we look for the remote control
            public double RemoteDistance;

            /// How far from the merge block should we look for the gyroscope
            public double GyroDistance;

            /// How far from the merge block should we look for functional blocks
            public double MaxComponentDistance;


            /// How far should the missile be from its launching merge block before guidance kicks
            /// in
            public double LaunchDistance = 7.5;

            /// What thrust percentage should we use when launching a missile.  On servers with
            /// high speed limits, missiles with a high acceleration using a high launch thrust can
            /// carry a lot of momentum, and as guidance kicks in this has to be compensated for.
            /// Sometimes a gentle *poopf* is enough to get them far enough away, and then guidance
            /// can kick in sooner.
            public float LaunchThrust = 0.05f;

            /// N-gain value for proportional navigation.  Should be between 3-5.  Lower values
            /// mean less effort is exerted to get on course, whereas higher values exert more
            /// force to get on course earlier, resulting in better hit rates for some missiles.
            public double PNGain = 4.0;

            /// How fast we should turn the gyroscope towards the target
            public double GyroGain = 36;

            /// How quickly we should slow down when approaching the target
            public double GyroDampingGain = 0.3;

            /// How far should we be from the target before arming the missiles
            public double ArmDistance = 75.0;

            /// Detonate warheads on the missile when this close to the target
            public double DetonateDistance = 2.5;

            /// How high should the missile fly if we are cruising?
            public double CruiseAltitude = 500.0;

            public MissileConfig Clone()
            {
                return MemberwiseClone() as MissileConfig;
            }
        }
    }
}
