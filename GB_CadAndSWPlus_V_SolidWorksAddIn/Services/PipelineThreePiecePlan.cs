using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 三点直角路线的实际毫米尺寸几何试算，不代表符合工程规范或存在真实构件。
    /// 不自动生成、保存、插入模型，也不写入数据库。
    /// </summary>
    internal sealed class PipelineThreePiecePlan
    {
        private const double RelativePrecisionTolerance = 1e-10;

        private PipelineThreePiecePlan(PipelineElbowGeometryResult elbow, double outsideDiameterMm,
            double wallThicknessMm, double insideDiameterMm)
        {
            Elbow = elbow;
            InletRoute = new PipeRoute(new[] { elbow.Start, elbow.EntryTangent });
            OutletRoute = new PipeRoute(new[] { elbow.ExitTangent, elbow.End });
            OutsideDiameterMm = outsideDiameterMm;
            WallThicknessMm = wallThicknessMm;
            InsideDiameterMm = insideDiameterMm;
        }

        public PipeRoute InletRoute { get; }
        public PipeRoute OutletRoute { get; }
        public PipelineElbowGeometryResult Elbow { get; }
        public double OutsideDiameterMm { get; }
        public double WallThicknessMm { get; }
        public double InsideDiameterMm { get; }

        internal static PipelineThreePiecePlan Create(PipeRoute route, double radiusMm,
            double outsideDiameterMm, double wallThicknessMm)
        {
            if (route == null) throw new ArgumentNullException(nameof(route), "路线不能为空。");
            if (!IsFinite(radiusMm) || radiusMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(radiusMm), "中心线半径必须为有限且大于零的毫米数值。");
            if (!IsFinite(outsideDiameterMm) || outsideDiameterMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(outsideDiameterMm), "外径必须为有限且大于零的毫米数值。");
            if (!IsFinite(wallThicknessMm) || wallThicknessMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(wallThicknessMm), "壁厚必须为有限且大于零的毫米数值。");

            double outsideRadius = outsideDiameterMm / 2.0;
            if (outsideRadius == 0 || outsideRadius * 2.0 != outsideDiameterMm)
                throw new ArithmeticException("外径半径的表示精度不足。");
            if (wallThicknessMm >= outsideRadius)
                throw new ArgumentOutOfRangeException(nameof(wallThicknessMm), "壁厚必须小于外径半径，内径必须大于零。");
            if (radiusMm <= outsideRadius)
                throw new ArgumentOutOfRangeException(nameof(radiusMm), "中心线半径必须大于外径半径，避免扫掠退化。");

            // 校验内径以及由内径反推的壁厚，防止极薄壁被浮点舍入吞没。
            double insideDiameter = outsideDiameterMm - 2.0 * wallThicknessMm;
            double insideRadius = insideDiameter / 2.0;
            if (insideDiameter <= 0 || insideRadius == 0 || insideRadius * 2.0 != insideDiameter ||
                Math.Abs(((outsideDiameterMm - insideDiameter) / 2.0) / wallThicknessMm - 1.0) > RelativePrecisionTolerance)
                throw new ArithmeticException("内径或壁厚的表示精度不足。");

            // 复核弯头内外侧极值，避免外径相对中心线半径过小或坐标溢出。
            double outerExtent = radiusMm + outsideRadius;
            double innerExtent = radiusMm - outsideRadius;
            if (!IsFinite(outerExtent) ||
                Math.Abs((outerExtent - radiusMm) / outsideRadius - 1.0) > RelativePrecisionTolerance ||
                Math.Abs((radiusMm - innerExtent) / outsideRadius - 1.0) > RelativePrecisionTolerance)
                throw new ArithmeticException("弯头扫掠尺寸溢出或表示精度不足。");

            PipelineElbowGeometryResult elbow = PipelineElbowGeometry.Calculate(route, radiusMm);
            return new PipelineThreePiecePlan(elbow, outsideDiameterMm, wallThicknessMm, insideDiameter);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
