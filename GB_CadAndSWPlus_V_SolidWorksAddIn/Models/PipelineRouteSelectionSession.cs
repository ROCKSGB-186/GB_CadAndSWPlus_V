using System.Collections.Generic;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Models
{
    internal enum PipelineRouteSelectionStatus
    {
        Selecting,
        Completed,
        Cancelled
    }

    /// <summary>管理一次自由端路线选点；不持有 SolidWorks 对象，也不管理界面事件。</summary>
    internal sealed class PipelineRouteSelectionSession
    {
        // 选点撤销只删除最后一个点，无需为每次操作保存完整点列历史。
        private PipeRouteDraft _draft = new PipeRouteDraft(false);
        private PipeWorkPlane _initialPlane = new PipeWorkPlane(PipeWorkPlaneKind.XY, 0);

        public PipelineRouteSelectionStatus Status { get; private set; } = PipelineRouteSelectionStatus.Selecting;
        public IReadOnlyList<PipePoint3D> Points => _draft.Points;
        public PipeWorkPlane WorkPlane { get; private set; } = new PipeWorkPlane(PipeWorkPlaneKind.XY, 0);

        /// <summary>切换工作平面并设置其固定坐标，坐标单位为毫米。</summary>
        public bool TrySetWorkPlane(PipeWorkPlane plane, out string error)
        {
            if (!EnsureSelecting(out error)) return false;
            // 重新构造以验证传入的枚举和有限坐标。
            var validated = new PipeWorkPlane(plane.Kind, plane.FixedCoordinateMm);
            _initialPlane = validated;
            WorkPlane = validated;
            error = string.Empty;
            return true;
        }

        public bool TryAddPoint(PipePoint3D point, out string error)
        {
            if (!EnsureSelecting(out error)) return false;
            IReadOnlyList<PipePoint3D> points = _draft.Points;
            if (points.Count > 0 && points[points.Count - 1] == point)
            {
                error = "新坐标与上一坐标重合，不能形成有效管段。";
                return false;
            }
            _draft.Add(point);
            FollowPoint(point);
            error = string.Empty;
            return true;
        }

        public bool TryEditLastPoint(PipePoint3D point, out string error)
        {
            if (!EnsureSelecting(out error)) return false;
            IReadOnlyList<PipePoint3D> points = _draft.Points;
            if (points.Count == 0)
            {
                error = "尚未选择坐标，无法编辑。";
                return false;
            }
            if (points.Count > 1 && points[points.Count - 2] == point)
            {
                error = "修改后的坐标与前一坐标重合，不能形成有效管段。";
                return false;
            }
            _draft.Edit(points.Count - 1, point);
            FollowPoint(point);
            error = string.Empty;
            return true;
        }

        /// <summary>Z 撤销最后一个已确认点，而不是撤销最近一次编辑操作。</summary>
        public bool TryRemoveLastPoint(out string error)
        {
            if (!EnsureSelecting(out error)) return false;
            int count = _draft.Points.Count;
            if (count == 0)
            {
                error = "尚未选择坐标，无法撤销。";
                return false;
            }
            _draft.RemoveAt(count - 1);
            IReadOnlyList<PipePoint3D> remaining = _draft.Points;
            if (remaining.Count == 0) WorkPlane = _initialPlane;
            else FollowPoint(remaining[remaining.Count - 1]);
            error = string.Empty;
            return true;
        }

        private void FollowPoint(PipePoint3D point)
        {
            double fixedCoordinate = WorkPlane.Kind == PipeWorkPlaneKind.XY ? point.Z
                : WorkPlane.Kind == PipeWorkPlaneKind.XZ ? point.Y : point.X;
            WorkPlane = new PipeWorkPlane(WorkPlane.Kind, fixedCoordinate);
        }

        public bool TryComplete(out PipeRoute? route, out string error)
        {
            route = null;
            if (!EnsureSelecting(out error)) return false;
            if (!_draft.TryCreateRoute(out route, out error)) return false;
            Status = PipelineRouteSelectionStatus.Completed;
            return true;
        }

        public bool TryCancel(out string error)
        {
            if (!EnsureSelecting(out error)) return false;
            _draft = new PipeRouteDraft(false);
            Status = PipelineRouteSelectionStatus.Cancelled;
            error = string.Empty;
            return true;
        }

        private bool EnsureSelecting(out string error)
        {
            if (Status != PipelineRouteSelectionStatus.Selecting)
            {
                error = "本次路线选点已经结束，不能继续修改。";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }
}
