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
        enum TargetType
        {
            Static,
            Tracked,
        }

        // A class containing interesting information about a target
        class TargetInfo
        {
            public TargetInfo(long entityId, TargetType targetType, MyRelationsBetweenPlayerAndBlock relationship)
            {
                EntityId = entityId;
                TargetType = targetType;
                Relationship = relationship;
            }

            public long EntityId { get; private set; }
            public double LastSeen { get; private set; }
            public TargetType TargetType { get; private set; }
            public MyRelationsBetweenPlayerAndBlock Relationship { get; private set; }

            public Vector3D Position { get; private set; }
            public Vector3D Velocity { get; private set; }

            public Vector3D Acceleration { get; private set; }

            public Vector3D LastKnownPosition { get; private set; }
            public Vector3D LastKnownVelocity { get; private set; }

            public void Track(Vector3D position, Vector3D velocity, double timestamp)
            {
                Acceleration = (LastKnownVelocity - velocity) / (timestamp - LastSeen);
                LastKnownPosition = Position = position;
                LastKnownVelocity = Velocity = velocity;
                LastSeen = timestamp;
            }

            public void Predict(double timestamp)
            {
                // TODO: Factor in acceleration here... and TODO (CIWS2), I think 'timestamp' in the 
                // below calculation should be '(timestamp - LastSeen)'...
                Position = LastKnownPosition + LastKnownVelocity * timestamp / 1000.0;
            }
        }
    }
}
