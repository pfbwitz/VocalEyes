using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VocalEyes.Common.Enumeration;
using VocalEyes.Common.Utils;
using OpenCV.Core;
using OpenCV.ImgProc;
using OpenCV.ObjDetect;
using VocalEyes.Common;
using VocalEyes.Droid.Activities;
using VocalEyes.Droid.Common.Model;
using VocalEyes.Model;

namespace VocalEyes.Droid.Common.Helper
{
    public class DetectionHelper
    {
        const int PupilRadius = 10;

        public bool Handling;

        private readonly CaptureActivity _activity;

        public const int JavaDetector = 0;

        public Point CenterPoint { get; private set; }

        private readonly List<Point> _centerpoints = new List<Point>();

        private readonly List<double> _averageAngles = new List<double>();
        public double? AverageAngle;

        private Pupil _leftPupil;

        private Pupil _rightPupil;

        private Eye _leftEye;

        private Eye _rightEye;

        public DetectionHelper(CaptureActivity activity)
        {
            _activity = activity;
        }

        public Rect GetNearestFace(IEnumerable<Rect> faces)
        {
            Rect face = null;
            foreach (var f in faces.ToArray())
            {
                if (face == null || f.Area() > face.Area())
                    face = f;
            }
            return face;
        }

        public Mat DetectLeftEye(CascadeClassifier clasificator, Rect area, Rect face, int size, out Direction direction)
        {
            if(_leftEye == null)
                _leftEye = new Eye(Constants.PupilSkip);

            if (_leftPupil == null)
                _leftPupil = new Pupil(Constants.PupilSkip);
            return DetectEye(clasificator, area, face, size, out direction);
        }

        public async Task ResetCalibration()
        {
            App.User.Calibrated = false;
            App.User.Save();

            _activity.Calibrating = true;
            CenterPoint = null;
            
            await _activity.Start();
        }

        private void Calibrate(Point point)
        {
            if (!_activity.Calibrating)
                return;

            var calibrationTime = _activity.FramesPerSecond * 2;

            _centerpoints.Add(point);
               
            if (_centerpoints.Count >= calibrationTime)
            {
                var cut = _centerpoints.Count / 2;
                while (_centerpoints.Count > cut)
                    _centerpoints.RemoveAt(0);

                CenterPoint = new Point(_centerpoints.Average(c => c.X), _centerpoints.Average(c => c.Y));
                App.User.CenterX = CenterPoint.X;
                App.User.CenterY = CenterPoint.Y;
                _activity.TextToSpeechHelper.Speak(SpeechHelper.GetDirectionString(Direction.Left));

                _activity.TextToSpeechHelper.Speak(SpeechHelper.CalibrationComplete);
                _activity.Calibrating = false;
                App.User.Calibrated = true;
                App.User.Save();
            }
        }

        private void DrawCalibrationPoints(Mat vyrez)
        {
            if (CenterPoint == null)
                return;

            var color = new Scalar(255, 0, 0);
            const int thickness = 2;

            Imgproc.Circle(vyrez, CenterPoint, PupilRadius, color, thickness);
        }

        public Mat DetectEye(CascadeClassifier clasificator, Rect area, Rect face, int size, out Direction direction)
        {
            var template = new Mat();
            var mRoi = _activity.MGray.Submat(area);
            var eyes = new MatOfRect();
            var areaMat = _activity.MRgba.Submat(area);

            clasificator.DetectMultiScale(mRoi, eyes, 1.15, 2, Objdetect.CascadeFindBiggestObject | Objdetect.CascadeScaleImage, new Size(30, 30), new Size());

            var eyesArray = eyes.ToArray();

            for (var i = 0; i < eyesArray.Length;)
            {
                var eye = eyesArray[i];
                eye.X = area.X + eye.X;
                eye.Y = area.Y + eye.Y;

                var eyeOnlyRectangle = _leftEye.Insert(new Rect((int) eye.Tl().X, (int) (eye.Tl().Y + eye.Height*0.4), 
                    eye.Width, (int) (eye.Height*0.6))).GetShape();
               
                mRoi = _activity.MGray.Submat(eyeOnlyRectangle);

                var mmG = Core.MinMaxLoc(mRoi);
                var cp = new Point(mmG.MinLoc.X + PupilRadius/2 + (eyeOnlyRectangle.X - area.X), 
                    mmG.MinLoc.Y + PupilRadius/2 + (eyeOnlyRectangle.Y - area.Y));

                Imgproc.Rectangle(_activity.MRgba, eyeOnlyRectangle.Tl(), eyeOnlyRectangle.Br(), new Scalar(255, 255, 255), 2);
               
                var avg = _leftPupil.Insert(cp).GetShape();
                Calibrate(avg);
                DrawCalibrationPoints(areaMat);
                    
                HandlePosition(areaMat, avg, out direction);
              
                Imgproc.Circle(areaMat, avg, PupilRadius, new Scalar(255, 255, 255), 2);
                Imgproc.Circle(areaMat, avg, 4, new Scalar(255, 255, 255), 2);
                Imgproc.Circle(areaMat, avg, 3, new Scalar(255, 255, 255), 2);

                return template;
            }
            direction = Direction.Center;
            return template;
        }

        private void HandlePosition(Mat areaMat, Point position, out Direction direction)
        {
            direction = Direction.Center;
            if (CenterPoint == null)
                return;
            Imgproc.Line(areaMat, position, CenterPoint, new Scalar(255, 255, 255), 1);

            var origin = new Point(0, 0);

            var distanceToCenter = TrigonometryHelper.GetHypetonuse(TrigonometryHelper.GetDistance(position.X, CenterPoint.X),
                TrigonometryHelper.GetDistance(position.Y, CenterPoint.Y));
            var hypotenuseCenter = TrigonometryHelper.GetHypetonuse(TrigonometryHelper.GetDistance(origin.X, CenterPoint.X),
                    TrigonometryHelper.GetDistance(origin.Y, CenterPoint.Y));

            var opposite = TrigonometryHelper.GetDistance(position.X, origin.X);
            var adjacent = TrigonometryHelper.GetDistance(position.Y, origin.Y);
            var hypotenuse = TrigonometryHelper.GetHypetonuse(opposite, adjacent);
            var angle = TrigonometryHelper.GetAngleTangent(opposite, adjacent);

            if (  _averageAngles.Count < _activity.FramesPerSecond * 2)
            {
                _averageAngles.Add(angle);
                return;
            }
            if (!AverageAngle.HasValue)
                AverageAngle = _averageAngles.Average();

            Imgproc.Line(areaMat, position, origin, new Scalar(0, 255, 0), 2);
            Imgproc.Line(areaMat, origin, new Point(position.X, origin.Y), new Scalar(0, 255, 0), 2);
            Imgproc.Line(areaMat, new Point(position.X, origin.Y), position, new Scalar(0, 255, 0), 2);

            Imgproc.Line(areaMat, CenterPoint, origin, new Scalar(255, 255, 255), 1);

           

            _activity.PutOutlinedText(areaMat, Convert.ToInt32(opposite) + " px", position.X / 2, origin.Y + 15);
            _activity.PutOutlinedText(areaMat, Convert.ToInt32(hypotenuse) + " px", position.X / 2, position.Y / 2 + 5);
            _activity.PutOutlinedText(areaMat, Convert.ToInt32(adjacent) + " px", position.X + 5, opposite / 2);
            _activity.PutOutlinedText(areaMat, Convert.ToInt32(angle) + " deg", position.X + 20, position.Y + 20);

            _activity.PutOutlinedText(areaMat, Convert.ToInt32(distanceToCenter) + " px", CenterPoint.X + 15, CenterPoint.Y + 15);

            if (_activity.ShouldAct())
            {
                try
                {
                    _activity.TextToSpeechHelper.CancelSpeak();

                    if (Handling)
                        return;

                    Handling = true;

                    if (_activity.Calibrating)
                        return;

                    direction = GetDirection(angle, hypotenuse, distanceToCenter, hypotenuseCenter);

                    //if (!direction.HasValue)
                    //    return;

                    //TextToSpeechHelper.Speak(direction.ToString());

                    //var emptyDirection = direction == Direction.Left || direction == Direction.Right ||
                    //                     direction == Direction.Center;

                    //if (_captureMethod == CaptureMethod.Subset && !emptyDirection)
                    //{
                    //    _currentSubset = SubSets.SingleOrDefault(s => s.Direction == direction);
                    //    if (_currentSubset == null)
                    //        return;

                    //    TextToSpeechHelper.Speak(direction.ToString());
                    //    _captureMethod = CaptureMethod.Character;
                    //}
                    //else if (_captureMethod == CaptureMethod.Character)
                    //{
                    //    var character = _currentSubset.GetCharacter(direction);
                    //    CharacterBuffer.Add(character);
                    //}
                    //else
                    //{
                    //    TextToSpeechHelper.Speak(string.Join("", CharacterBuffer));
                    //    CharacterBuffer.Clear();
                    //}
                }
                catch (Exception ex)
                {
                    _activity.HandleException(ex);
                }
                finally
                {
                    Handling = false;
                }
            }
        }

        private Direction GetDirection(double angle, double hypotenuse, double distanceToCenter, double hypotenuseCenter)
        {
            var direction = Direction.Center;
            var angleMargin = 30;
            if (angle < AverageAngle) //gaze inward
            {
                if (_activity.Facing == CameraFacing.Back) // right
                {
                    direction = Direction.Right;
                    if (angle < angleMargin)
                        direction = Direction.TopRight;
                    else if (angle > angleMargin)
                        direction = Direction.BottomRight;
                }
                else // left
                {
                    direction = Direction.Left;
                    if (angle < angleMargin)
                        direction = Direction.TopLeft;
                    else if (angle > angleMargin)
                        direction = Direction.BottomLeft;
                }
            }
            else if (TrigonometryHelper.GetDistance(angle, AverageAngle.Value) <= AverageAngle.Value + 5) //center gaze
            {
                if (hypotenuse + PupilRadius < hypotenuseCenter)
                {
                    direction = Direction.Top;
                }
                else if (hypotenuse + PupilRadius > hypotenuseCenter)
                {
                    direction = Direction.Bottom;
                }
            }
            else //outward gaze
            {
                if (_activity.Facing == CameraFacing.Back) // left
                {
                    direction = Direction.Left;
                    if (angle < angleMargin)
                        direction = Direction.TopLeft;
                    else if (angle > angleMargin)
                        direction = Direction.BottomLeft;
                }
                else // right
                {
                    direction = Direction.Right;
                    if (angle < angleMargin)
                        direction = Direction.TopRight;
                    else if (angle > angleMargin)
                        direction = Direction.BottomRight;
                }
            }

            return direction;
        }

        /// <summary>
        /// Determine the absolute distance between 2 points
        /// </summary>
        /// <param name="direction"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static DirectionDistance GetDistance(Direction direction, Point a, Point b)
        {
            var distanceX = a.X - b.X;
            if (distanceX < 0)
                distanceX = distanceX*-1;

            var distanceY = a.Y - b.Y;
            if (distanceY < 0)
                distanceY = distanceY*-1;
            return new DirectionDistance
            {
                Direction = direction, Distance = Math.Sqrt(Math.Pow(distanceX, 2) + Math.Pow(distanceY, 2))
            };
        }

        /// <summary>
        /// Compare the pupil position and the center position 
        /// and determine the direction of the pupil
        /// </summary>
        /// <param name="point">pupil position</param>
        /// <returns>Direction of pupil</returns>
        public Direction? GetDirection(Point point)
        {
            if (_activity.Calibrating)
                return null;
            var distances = new List<DirectionDistance>
            {
                GetDistance(Direction.Center, point, CenterPoint)
            };

            return distances.OrderBy(d => d.Distance).First().Direction;
        }
    }
}