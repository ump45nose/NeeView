// Copyright (c) NeeLaboratory. 从固定 Windows 基线迁入，平台值类型适配。
using System;
using System.Collections.Generic;


namespace NeeView
{
    /// <summary>
    /// ジェスチャー生成
    /// </summary>
    public class MouseSequenceBuilder
    {
        /// <summary>
        /// ジェスチャー方向ベクトル
        /// </summary>
        private static readonly Dictionary<MouseDirection, Vector> _gestureDirectionVector = new()
        {
            [MouseDirection.None] = new Vector(0, 0),
            [MouseDirection.Up] = new Vector(0, -1),
            [MouseDirection.Right] = new Vector(1, 0),
            [MouseDirection.Down] = new Vector(0, 1),
            [MouseDirection.Left] = new Vector(-1, 0)
        };


        private readonly List<MouseDirection> _gestures = new();
        private MouseDirection _direction;
        private Vector _origin;


        public MouseSequenceBuilder()
        {
        }


        /// <summary>
        /// ジェスチャー進捗通知
        /// </summary>
        public event EventHandler<MouseGestureEventArgs>? GestureProgressed;


        public bool IsEmpty => _gestures.Count == 0;


        /// <summary>
        /// ジェスチャーシーケンス
        /// </summary>
        public MouseSequence ToMouseSequence() => new MouseSequence(_gestures);


        private void RaiseGestureProgressed()
        {
            GestureProgressed?.Invoke(this, new MouseGestureEventArgs(ToMouseSequence()));
        }

        /// <summary>
        /// 初期化
        /// </summary>
        public void Reset(Vector point)
        {
            _direction = MouseDirection.None;
            _gestures.Clear();
            RaiseGestureProgressed();

            _origin = point;
        }

        /// <summary>
        /// 入力更新
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void Move(Vector point)
        {
            var v1 = new Vector(point.X - _origin.X, point.Y - _origin.Y);

            // 一定距離未満は判定しない
            if (Math.Abs(v1.X) < Config.Current.Mouse.GestureMinimumDistance && Math.Abs(v1.Y) < Config.Current.Mouse.GestureMinimumDistance) return;

            // 方向を決める
            // 斜め方向は以前の方向とする
            if (_direction != MouseDirection.None && Math.Abs(AngleBetween(_gestureDirectionVector[_direction], v1)) < 60)
            {
                // そのまま
            }
            else
            {
                foreach (MouseDirection direction in _gestureDirectionVector.Keys)
                {
                    if (direction != MouseDirection.None && Math.Abs(AngleBetween(_gestureDirectionVector[direction], v1)) < 30)
                    {
                        _direction = direction;
                        _gestures.Add(_direction);
                        RaiseGestureProgressed();
                        break;
                    }
                }
            }

            // 開始点の更新
            _origin = point;
        }

        /// <summary>
        /// 入力更新 (クリック)
        /// </summary>
        public void AddClick()
        {
            if (_gestures.Count > 0)
            {
                _gestures.Add(MouseDirection.Click);
                RaiseGestureProgressed();
            }
        }
        /// <summary>替换 WPF Vector.AngleBetween，保持原方向夹角判定。</summary>
        private static double AngleBetween(Vector a, Vector b) => Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y) * 180 / Math.PI;
    }
}
