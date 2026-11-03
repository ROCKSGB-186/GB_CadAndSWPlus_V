using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    /// <summary>以毫米为单位的有限三维坐标。</summary>
    public struct PipePoint3D : IEquatable<PipePoint3D>
    {
        public PipePoint3D(double x, double y, double z)
        {
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            RequireFinite(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public bool Equals(PipePoint3D other) { return X == other.X && Y == other.Y && Z == other.Z; }
        public override bool Equals(object? obj) { return obj is PipePoint3D && Equals((PipePoint3D)obj); }
        public override int GetHashCode() { unchecked { return ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); } }
        public static bool operator ==(PipePoint3D left, PipePoint3D right) { return left.Equals(right); }
        public static bool operator !=(PipePoint3D left, PipePoint3D right) { return !left.Equals(right); }

        internal static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "数值必须为有限值。");
        }
    }

    /// <summary>有限方向向量；不要求归一化，但射线不接受零向量。</summary>
    public struct PipeDirection3D
    {
        public PipeDirection3D(double x, double y, double z)
        {
            PipePoint3D.RequireFinite(x, nameof(x));
            PipePoint3D.RequireFinite(y, nameof(y));
            PipePoint3D.RequireFinite(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    /// <summary>连接端仅为后续扩展预留；当前没有可创建的连接端。</summary>
    public enum PipeEndpointKind
    {
        Free,
        ConnectionReserved
    }

    /// <summary>已验证的自由端路线快照，不依赖 SolidWorks 对象。</summary>
    public sealed class PipeRoute
    {
        internal PipeRoute(IList<PipePoint3D> points)
        {
            Points = new ReadOnlyCollection<PipePoint3D>(new List<PipePoint3D>(points));
        }

        public IReadOnlyList<PipePoint3D> Points { get; }
        public PipeEndpointKind StartKind { get { return PipeEndpointKind.Free; } }
        public PipeEndpointKind EndKind { get { return PipeEndpointKind.Free; } }
    }

    /// <summary>允许不完整的路线草稿；只有满足约束才能创建路线快照。</summary>
    public sealed class PipeRouteDraft
    {
        private readonly List<PipePoint3D> points = new List<PipePoint3D>();
        private readonly Stack<List<PipePoint3D>> history = new Stack<List<PipePoint3D>>();
        private readonly bool trackHistory;

        public PipeRouteDraft() : this(true) { }

        internal PipeRouteDraft(bool trackHistory) { this.trackHistory = trackHistory; }

        /// <summary>返回独立快照，外部修改不会影响草稿。</summary>
        public IReadOnlyList<PipePoint3D> Points
        {
            get { return new ReadOnlyCollection<PipePoint3D>(new List<PipePoint3D>(points)); }
        }

        public bool CanUndo { get { return history.Count != 0; } }

        public void Add(PipePoint3D point) { Insert(points.Count, point); }

        public void Insert(int index, PipePoint3D point)
        {
            if (index < 0 || index > points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            SaveHistory();
            points.Insert(index, point);
        }

        public void Edit(int index, PipePoint3D point)
        {
            if (index < 0 || index >= points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (points[index] == point) return;
            SaveHistory();
            points[index] = point;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            SaveHistory();
            points.RemoveAt(index);
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            List<PipePoint3D> previous = history.Pop();
            points.Clear();
            points.AddRange(previous);
            return true;
        }

        /// <summary>验证至少两个不同点且没有零长度相邻线段，失败时不产生路线。</summary>
        public bool TryCreateRoute(out PipeRoute? route, out string error)
        {
            route = null;
            if (points.Count < 2)
            {
                error = "路线至少需要两个不同的点。";
                return false;
            }

            bool hasDifferentPoint = false;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i] == points[i - 1])
                {
                    error = "路线不能包含相邻的重复点。";
                    return false;
                }
                if (points[i] != points[0]) hasDifferentPoint = true;
            }
            if (!hasDifferentPoint)
            {
                error = "路线至少需要两个不同的点。";
                return false;
            }

            route = new PipeRoute(points);
            error = string.Empty;
            return true;
        }

        private void SaveHistory()
        {
            if (trackHistory) history.Push(new List<PipePoint3D>(points));
        }
    }
}
