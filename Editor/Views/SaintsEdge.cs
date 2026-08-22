using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>Edge that draws itself in the style chosen in preferences.</summary>
    internal class SaintsEdge : Edge
    {
        protected override EdgeControl CreateEdgeControl()
        {
            return new SaintsEdgeControl
            {
                capRadius = 4f,
                interceptWidth = 6f
            };
        }

        /// <summary>Re-reads the preferences without rebuilding the edge.</summary>
        public void RefreshStyle()
        {
            if (edgeControl is SaintsEdgeControl control)
            {
                control.RefreshStyle();
            }
        }
    }

    /// <summary>
    /// Draws the connection itself instead of using GraphView's fixed right-angle routing.
    /// Unity keeps its render points private, so the whole path is generated here — which is also
    /// what makes hit-testing follow the drawn shape rather than the default one.
    /// </summary>
    internal class SaintsEdgeControl : EdgeControl
    {
        private const int CurveSegments = 24;

        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly Gradient _gradient = new Gradient();
        private NoodleStyle _style = SaintsGraphPreferences.NoodleStyle;

        public SaintsEdgeControl()
        {
            // Replaces GraphView's own draw callback; its private render points are not reachable.
            generateVisualContent = Draw;
        }

        public void RefreshStyle()
        {
            _style = SaintsGraphPreferences.NoodleStyle;
            MarkDirtyRepaint();
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (_points.Count < 2)
            {
                return base.ContainsPoint(localPoint);
            }

            float threshold = Mathf.Max(interceptWidth, edgeWidth);
            for (int i = 1; i < _points.Count; i++)
            {
                if (DistanceToSegment(localPoint, _points[i - 1], _points[i]) <= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        public override bool Overlaps(Rect rect)
        {
            if (_points.Count < 2)
            {
                return base.Overlaps(rect);
            }

            foreach (Vector2 point in _points)
            {
                if (rect.Contains(point))
                {
                    return true;
                }
            }

            // A long segment can cross the rectangle without either end being inside it.
            for (int i = 1; i < _points.Count; i++)
            {
                if (SegmentIntersectsRect(_points[i - 1], _points[i], rect))
                {
                    return true;
                }
            }

            return false;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (parent == null || edgeWidth <= 0)
            {
                return;
            }

            Vector2 start = parent.ChangeCoordinatesTo(this, from);
            Vector2 end = parent.ChangeCoordinatesTo(this, to);
            BuildPath(start, end);
            if (_points.Count < 2)
            {
                return;
            }

            _gradient.SetKeys(
                new[] { new GradientColorKey(outputColor, 0f), new GradientColorKey(inputColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f) });

            Painter2D painter = context.painter2D;
            painter.BeginPath();
            painter.strokeGradient = _gradient;
            painter.lineWidth = Mathf.Max(edgeWidth, 2f);
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.MoveTo(_points[0]);
            for (int i = 1; i < _points.Count; i++)
            {
                painter.LineTo(_points[i]);
            }

            painter.Stroke();
        }

        private void BuildPath(Vector2 start, Vector2 end)
        {
            _points.Clear();
            switch (_style)
            {
                case NoodleStyle.Curvy:
                    BuildCurvy(start, end);
                    break;

                case NoodleStyle.Straight:
                    _points.Add(start);
                    _points.Add(end);
                    break;

                case NoodleStyle.Angled:
                    BuildAngled(start, end);
                    break;

                default:
                    BuildRounded(start, end);
                    break;
            }
        }

        /// <summary>
        /// The shape GraphView draws by default: a short horizontal stub at each port, a diagonal
        /// between them, and rounded corners where they meet. Reimplemented here because taking
        /// over the drawing is the only way to offer any other style at all.
        /// </summary>
        private void BuildRounded(Vector2 start, Vector2 end)
        {
            float span = Mathf.Abs(end.x - start.x);
            float stub = Mathf.Clamp(span * 0.25f, 12f, 28f);
            Vector2 a = start + Vector2.right * stub;
            Vector2 b = end - Vector2.right * stub;
            float radius = Mathf.Min(16f, (a - b).magnitude * 0.4f);

            _points.Add(start);
            AddCorner(start, a, b, radius);
            AddCorner(a, b, end, radius);
            _points.Add(end);
        }

        private void BuildCurvy(Vector2 start, Vector2 end)
        {
            // Leaving each port horizontally reads as "flow", and keeps the curve clear of the node
            // even when the target sits behind the source.
            float reach = Mathf.Max(Mathf.Abs(end.x - start.x) * 0.5f, 40f);
            Vector2 c1 = start + Vector2.right * reach;
            Vector2 c2 = end - Vector2.right * reach;

            for (int i = 0; i <= CurveSegments; i++)
            {
                float t = i / (float)CurveSegments;
                float inv = 1f - t;
                Vector2 point = inv * inv * inv * start
                                + 3f * inv * inv * t * c1
                                + 3f * inv * t * t * c2
                                + t * t * t * end;
                _points.Add(point);
            }
        }

        private void BuildAngled(Vector2 start, Vector2 end)
        {
            float stub = 12f;
            Vector2 a = start + Vector2.right * stub;
            Vector2 b = end - Vector2.right * stub;
            float midX = (a.x + b.x) * 0.5f;
            Vector2 corner1 = new Vector2(midX, a.y);
            Vector2 corner2 = new Vector2(midX, b.y);
            float radius = Mathf.Min(10f, Mathf.Abs(b.y - a.y) * 0.5f, Mathf.Abs(midX - a.x), Mathf.Abs(b.x - midX));

            _points.Add(start);
            _points.Add(a);
            AddCorner(a, corner1, corner2, radius);
            AddCorner(corner1, corner2, b, radius);
            _points.Add(b);
            _points.Add(end);
        }

        /// <summary>Softens the corner at <paramref name="corner"/> with a short quadratic arc.</summary>
        private void AddCorner(Vector2 previous, Vector2 corner, Vector2 next, float radius)
        {
            if (radius <= 0.5f)
            {
                _points.Add(corner);
                return;
            }

            Vector2 entry = corner + (previous - corner).normalized * radius;
            Vector2 exit = corner + (next - corner).normalized * radius;
            const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float inv = 1f - t;
                _points.Add(inv * inv * entry + 2f * inv * t * corner + t * t * exit);
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < Mathf.Epsilon)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            return SegmentsCross(a, b, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin))
                   || SegmentsCross(a, b, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax))
                   || SegmentsCross(a, b, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax))
                   || SegmentsCross(a, b, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin));
        }

        private static bool SegmentsCross(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float d1 = Cross(b2 - b1, a1 - b1);
            float d2 = Cross(b2 - b1, a2 - b1);
            float d3 = Cross(a2 - a1, b1 - a1);
            float d4 = Cross(a2 - a1, b2 - a1);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
