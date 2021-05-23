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
        private static void GyroTurn6(double ms, Vector3D directionVector, double gyroGain,
            double gyroDampingGain, IMyTerminalBlock forwardFacingBlock, List<IMyGyro> gyros,
            double YawPrev, double PitchPrev, out double NewPitch, out double NewYaw, bool spinning)
        {
            var ticks = ms / 1000.0;

            // We do need these values after all
            NewYaw = 0;
            NewPitch = 0;

            //Retrieving Forwards And Up
            Vector3D ShipUp = forwardFacingBlock.WorldMatrix.Up;
            Vector3D ShipForward = forwardFacingBlock.WorldMatrix.Forward;

            //Create And Use Inverse Quatinion
            Quaternion Quat_Two = Quaternion.CreateFromForwardUp(ShipForward, ShipUp);
            var InvQuat = Quaternion.Inverse(Quat_Two);

            Vector3D RCReferenceFrameVector = Vector3D.Transform(directionVector, InvQuat); //Target Vector In Terms Of RC Block

            //Convert To Local Azimuth And Elevation
            double ShipForwardAzimuth = 0;
            double ShipForwardElevation = 0;
            Vector3D.GetAzimuthAndElevation(RCReferenceFrameVector, out ShipForwardAzimuth, out ShipForwardElevation);

            //Post Setting Factors
            NewYaw = ShipForwardAzimuth;
            NewPitch = ShipForwardElevation;

            //Applies Some PID Damping
            ShipForwardAzimuth = ShipForwardAzimuth + gyroDampingGain * ((ShipForwardAzimuth - YawPrev) / ticks);
            ShipForwardElevation = ShipForwardElevation + gyroDampingGain * ((ShipForwardElevation - PitchPrev) / ticks);

            //Applies To Scenario
            foreach (var gyro in gyros)
            {
                //Does Some Rotations To Provide For any Gyro-Orientation
                var REF_Matrix = MatrixD.CreateWorld(gyro.CubeGrid.WorldVolume.Center, (Vector3)ShipForward, (Vector3)ShipUp).GetOrientation();
                var Vector = Vector3.Transform((new Vector3D(ShipForwardElevation, ShipForwardAzimuth, 0)), REF_Matrix); //Converts To World
                var TRANS_VECT = Vector3.Transform(Vector, Matrix.Transpose(gyro.WorldMatrix.GetOrientation()));  //Converts To Gyro Local

                //Logic Checks for NaN's
                if (double.IsNaN(TRANS_VECT.X) || double.IsNaN(TRANS_VECT.Y) || double.IsNaN(TRANS_VECT.Z))
                {
                    return;
                }

                gyro.Pitch = (float)MathHelper.Clamp((-TRANS_VECT.X) * gyroGain, -1000, 1000);
                gyro.Yaw = (float)MathHelper.Clamp(((-TRANS_VECT.Y)) * gyroGain, -1000, 1000);
                //gyro.Roll = (float)MathHelper.Clamp(((-TRANS_VECT.Z)) * gyroGain, -1000, 1000);
                gyro.Roll = spinning ? 1000f : (float)MathHelper.Clamp(((-TRANS_VECT.Z)) * gyroGain, -1000, 1000);
                gyro.GyroOverride = true;
            }
        }

        static Vector3D GetPredictedTargetPosition2(IMyTerminalBlock shooter, Vector3 ShipVel, TargetInfo target, float shotSpeed)
        {
            Vector3D predictedPosition = target.Position;
            Vector3D dirToTarget = Vector3D.Normalize(predictedPosition - shooter.GetPosition());

            //Run Setup Calculations
            Vector3 targetVelocity = target.Velocity;
            targetVelocity -= ShipVel;
            Vector3 targetVelOrth = Vector3.Dot(targetVelocity, dirToTarget) * dirToTarget;
            Vector3 targetVelTang = targetVelocity - targetVelOrth;
            Vector3 shotVelTang = targetVelTang;
            float shotVelSpeed = shotVelTang.Length();

            if (shotVelSpeed > shotSpeed)
            {
                // Shot is too slow
                return Vector3.Normalize(target.Velocity) * shotSpeed;
            }

            // Run Calculations
            float shotSpeedOrth = (float)Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeed * shotVelSpeed);
            Vector3 shotVelOrth = dirToTarget * shotSpeedOrth;
            float timeDiff = shotVelOrth.Length() - targetVelOrth.Length();
            var timeToCollision = timeDiff != 0 ? ((shooter.GetPosition() - target.Position).Length()) / timeDiff : 0;
            Vector3 shotVel = shotVelOrth + shotVelTang;
            predictedPosition = timeToCollision > 0.01f ? shooter.GetPosition() + (Vector3D)shotVel * timeToCollision + (0.5 * target.Acceleration.LengthSquared()) : predictedPosition;
            return predictedPosition;
        }
    }
}
