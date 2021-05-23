using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
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
        static readonly TimeSpan LOG_RATE = new TimeSpan(0, 0, 0, 0, 333);

        TimeSpan timeSinceLastLog = TimeSpan.Zero;
        StringBuilder log = new StringBuilder();
        IMyTextSurface LogSurface { get; set; }
        StringBuilder Log { get { return log; } }

        List<IMyLargeTurretBase> turretsToTryGetTimestampFrom;
        private bool TryGetTimestamp(ref long nextTimestamp)
        {
            if (nextTimestamp > 0)
            {
                // We have a timestamp, so simulate it
                return false;
            }

            if (turretsToTryGetTimestampFrom == null)
            {
                turretsToTryGetTimestampFrom = new List<IMyLargeTurretBase>();
                GetAll(turretsToTryGetTimestampFrom);
            }

            foreach (var turret in turretsToTryGetTimestampFrom)
            {
                var entity = turret.GetTargetedEntity();
                if (!entity.IsEmpty())
                {
                    nextTimestamp = entity.TimeStamp;
                    return true;

                }
            }
            return false;
        }

        void InitLog(IMyTextSurface surface,
                             float fontSize = 1f)
        {
            LogSurface = surface;
            LogSurface.FontSize = fontSize;
            LogSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            LogSurface.Font = "Debug";
        }

        char[] spinner = new char[] { '/', '-', '\\', '|' };
        int spinnerIndex = 0;
        static readonly int SPINNER_RATE = 3;

        void LogHeader(string name)
        {
            Log.AppendFormat("{0} {1}\n", name, spinner[spinnerIndex / SPINNER_RATE]);
            Log.AppendLine("===============================");
            spinnerIndex = (spinnerIndex + 1) % (spinner.Length * SPINNER_RATE);
        }

        void FlushLog(TimeSpan timeSinceLastRun)
        {
            timeSinceLastLog += timeSinceLastRun;
            if (timeSinceLastLog > LOG_RATE || timeSinceLastRun.TotalSeconds == 0)
            {
                LogSurface.WriteText(log, false);
                timeSinceLastLog = TimeSpan.Zero;
            }
            log.Clear();
        }

        T Get<T>(string name = "",
                           bool otherConstructs = false,
                           double distance = 0.0,
                           IMyEntity reference = null) where T : class
        {
            List<T> results = new List<T>();
            GridTerminalSystem.GetBlocksOfType(results, b =>
              (string.IsNullOrEmpty(name) || name.Equals((b as IMyTerminalBlock).DisplayNameText)) &&
              (otherConstructs || (b as IMyTerminalBlock).IsSameConstructAs(Me)) &&
              (distance == 0 || Vector3D.Distance((b as IMyTerminalBlock).GetPosition(), reference == null ? Me.GetPosition() : reference.GetPosition()) < distance)
            );

            if (results.Count() != 1)
            {
                StringBuilder err = new StringBuilder();
                err.AppendLine("Expected 1 block of type " + typeof(T));
                if (!string.IsNullOrEmpty(name))
                {
                    err.AppendLine(" named `").Append(name).Append("`");
                }
                if (distance > 0)
                {
                    err.AppendLine(" within " + distance + " meters");
                }
                err.AppendLine(", found " + results.Count());
                throw new Exception(err.ToString());
            }
            return results[0];
        }

        void GetGroup<T>(string groupName,
                                   List<T> results,
                                   bool otherConstructs = false,
                                   double distance = 0.0,
                                   IMyEntity reference = null) where T : class
        {
            GridTerminalSystem.GetBlockGroupWithName(groupName).GetBlocksOfType<T>(
              results, b =>
                (otherConstructs || (b as IMyTerminalBlock).IsSameConstructAs(Me)) &&
                (distance == 0 || Vector3D.Distance((b as IMyTerminalBlock).GetPosition(), reference == null ? Me.GetPosition() : reference.GetPosition()) < distance)
            );
        }

        void GetAll<T>(List<T> results,
                                 bool otherConstructs = false,
                                 double distance = 0.0,
                                 IMyEntity reference = null) where T : class
        {
            GridTerminalSystem.GetBlocksOfType<T>(
              results, b =>
                (otherConstructs || (b as IMyTerminalBlock).IsSameConstructAs(Me)) &&
                (distance == 0 || Vector3D.Distance((b as IMyTerminalBlock).GetPosition(), reference == null ? Me.GetPosition() : reference.GetPosition()) < distance)
            );
        }

        double ShortAmount(double amount, out char suffix)
        {
            if (amount < 1000)
            {
                suffix = 'x';
                return amount;
            }
            else if (amount < 1000000)
            {
                suffix = 'K';
                return amount / 1000.0;
            }
            else
            {
                suffix = 'M';
                return amount / 1000000.0;
            }
        }
    }
}
