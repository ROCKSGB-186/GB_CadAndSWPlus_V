using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    public enum PipeWorkPlaneKind { XY, XZ, YZ }

    /// <summary>固定坐标的世界坐标工作平面：XY 固定 Z，XZ 固定 Y，YZ 固定 X。</summary>
    public struct PipeWorkPlane
    {
        public PipeWorkPlane(PipeWorkPlaneKind kind, double fixedCoordinateMm)
        {
            if (kind != PipeWorkPlaneKind.XY && kind != PipeWorkPlaneKind.XZ && kind != PipeWorkPlaneKind.YZ)
                throw new ArgumentOutOfRangeException(nameof(kind));
            PipePoint3D.RequireFinite(fixedCoordinateMm, nameof(fixedCoordinateMm));
            Kind = kind;
            FixedCoordinateMm = fixedCoordinateMm;
        }

        public PipeWorkPlaneKind Kind { get; }
        public double FixedCoordinateMm { get; }
    }

    public enum PipeRayIntersectionResult
    {
        Hit,
        Parallel,
        BehindOrigin,
        NumericOverflow
    }

    public static class PipeRouteMath
    {
        /// <summary>
        /// 射线为 origin + t * direction（t >= 0）；方向不必归一化。
        /// 与平面法向夹角的余弦绝对值不超过 1e-10 时拒绝求交。
        /// 未命中时 intersection 无意义，必须先检查返回值。
        /// </summary>
        public static PipeRayIntersectionResult IntersectRay(
            PipePoint3D origin, PipeDirection3D direction, PipeWorkPlane plane, out PipePoint3D intersection)
        {
            intersection = default(PipePoint3D);
            double scale = Math.Max(Math.Abs(direction.X), Math.Max(Math.Abs(direction.Y), Math.Abs(direction.Z)));
            if (scale == 0) throw new ArgumentException("射线方向不能为零向量。", nameof(direction));

            // 先按最大分量缩放，避免归一化时平方溢出或下溢。
            double x = direction.X / scale;
            double y = direction.Y / scale;
            double z = direction.Z / scale;
            double length = Math.Sqrt(x * x + y * y + z * z);
            x /= length;
            y /= length;
            z /= length;
            double normal = plane.Kind == PipeWorkPlaneKind.XY ? z : plane.Kind == PipeWorkPlaneKind.XZ ? y : x;
            double originNormal = plane.Kind == PipeWorkPlaneKind.XY ? origin.Z : plane.Kind == PipeWorkPlaneKind.XZ ? origin.Y : origin.X;
            if (Math.Abs(normal) <= 1e-10) return PipeRayIntersectionResult.Parallel;

            double distance = (plane.FixedCoordinateMm - originNormal) / normal;
            if (double.IsInfinity(distance) || double.IsNaN(distance)) return PipeRayIntersectionResult.NumericOverflow;
            if (distance < 0) return PipeRayIntersectionResult.BehindOrigin;

            double hitX = plane.Kind == PipeWorkPlaneKind.YZ ? plane.FixedCoordinateMm : origin.X + distance * x;
            double hitY = plane.Kind == PipeWorkPlaneKind.XZ ? plane.FixedCoordinateMm : origin.Y + distance * y;
            double hitZ = plane.Kind == PipeWorkPlaneKind.XY ? plane.FixedCoordinateMm : origin.Z + distance * z;
            if (double.IsInfinity(hitX) || double.IsNaN(hitX) ||
                double.IsInfinity(hitY) || double.IsNaN(hitY) ||
                double.IsInfinity(hitZ) || double.IsNaN(hitZ))
                return PipeRayIntersectionResult.NumericOverflow;
            intersection = new PipePoint3D(hitX, hitY, hitZ);
            return PipeRayIntersectionResult.Hit;
        }

        /// <summary>
        /// 对齐原点为零的毫米网格；最近格点，中点远离零。
        /// 使用 decimal 运算，距半格不超过 1e-12 格视为中点，抵消常见浮点小数误差；
        /// 转换误差超过浮点数精度范围、格数超过 1e15 或超出 decimal 范围时抛出异常，
        /// 避免在数值精度不足时返回看似有效的吸附结果。
        /// </summary>
        public static double SnapCoordinate(double coordinateMm, decimal stepMm)
        {
            PipePoint3D.RequireFinite(coordinateMm, nameof(coordinateMm));
            if (stepMm <= 0m) throw new ArgumentOutOfRangeException(nameof(stepMm), "步长必须大于零。");
            decimal coordinate = (decimal)coordinateMm;
            if (coordinateMm != 0 && coordinate == 0m)
                throw new ArgumentOutOfRangeException(nameof(coordinateMm), "坐标超出可对齐的精度范围。");
            double conversionTolerance = 1e-12 + 8e-15 * Math.Abs(coordinateMm / (double)stepMm);
            if (conversionTolerance > 0.0001 ||
                Math.Abs(coordinateMm - (double)coordinate) / (double)stepMm > conversionTolerance)
                throw new ArgumentOutOfRangeException(nameof(coordinateMm), "坐标相对于步长的精度不足。");

            decimal units = coordinate / stepMm;
            decimal magnitude = Math.Abs(units);
            if (magnitude > 1000000000000000m)
                throw new ArgumentOutOfRangeException(nameof(coordinateMm), "坐标相对于步长的格数过大。");
            decimal whole = decimal.Floor(magnitude);
            decimal fraction = magnitude - whole;
            if (fraction >= 0.5m - 0.000000000001m) whole += 1m;
            decimal snapped = (units < 0m ? -whole : whole) * stepMm;
            double result = (double)snapped;
            if ((double)(Math.Abs((decimal)result - snapped) / stepMm) > conversionTolerance)
                throw new ArithmeticException("吸附结果相对于步长的精度不足。");
            return result;
        }

        /// <summary>三个坐标独立吸附至原点为零的网格；结果不保证仍在原工作平面上。</summary>
        public static PipePoint3D SnapPoint(PipePoint3D point, decimal stepMm)
        {
            return new PipePoint3D(
                SnapCoordinate(point.X, stepMm),
                SnapCoordinate(point.Y, stepMm),
                SnapCoordinate(point.Z, stepMm));
        }

        /// <summary>仅吸附平面内两个坐标，固定坐标始终使用工作平面的值。</summary>
        public static PipePoint3D SnapOnPlane(PipePoint3D point, PipeWorkPlane plane, decimal stepMm)
        {
            switch (plane.Kind)
            {
                case PipeWorkPlaneKind.XY:
                    return new PipePoint3D(SnapCoordinate(point.X, stepMm), SnapCoordinate(point.Y, stepMm), plane.FixedCoordinateMm);
                case PipeWorkPlaneKind.XZ:
                    return new PipePoint3D(SnapCoordinate(point.X, stepMm), plane.FixedCoordinateMm, SnapCoordinate(point.Z, stepMm));
                case PipeWorkPlaneKind.YZ:
                    return new PipePoint3D(plane.FixedCoordinateMm, SnapCoordinate(point.Y, stepMm), SnapCoordinate(point.Z, stepMm));
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }
    }
}
