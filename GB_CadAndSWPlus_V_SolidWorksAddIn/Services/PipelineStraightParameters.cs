using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>已校验的直管尺寸及本次确认属性快照，供试运行和后续装配流程共用。</summary>
    internal sealed class PipelineStraightParameters
    {
        private PipelineStraightParameters(double diameter, double thickness, Dictionary<string, string> attributes)
        {
            OutsideDiameterMm = diameter;
            WallThicknessMm = thickness;
            Attributes = new ReadOnlyDictionary<string, string>(attributes);
        }

        public double OutsideDiameterMm { get; }
        public double WallThicknessMm { get; }
        public IDictionary<string, string> Attributes { get; }

        public static bool TryCreate(IDictionary<string, string> attributes,
            out PipelineStraightParameters? parameters, out string error)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            parameters = null;
            var snapshot = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
            if (!snapshot.TryGetValue("PIPE_OD", out string diameterText) ||
                !snapshot.TryGetValue("PIPE_THK", out string thicknessText) ||
                !TryPositiveNumber(diameterText, out double diameter) ||
                !TryPositiveNumber(thicknessText, out double thickness) || thickness >= diameter / 2)
            {
                error = "外径和壁厚无效，请重新确认管道属性。";
                return false;
            }

            if (snapshot.TryGetValue("PIPE_ID", out string innerText) && !string.IsNullOrWhiteSpace(innerText))
            {
                double expected = (diameter - thickness) - thickness;
                if (!TryPositiveNumber(innerText, out double inner) ||
                    Math.Abs(inner - expected) > Math.Max(1e-8, expected * 1e-9))
                {
                    error = "输入的内径应等于外径减去两倍壁厚，请重新编辑属性或将内径留空。";
                    return false;
                }
            }

            parameters = new PipelineStraightParameters(diameter, thickness, snapshot);
            error = string.Empty;
            return true;
        }

        public static bool TryPositiveNumber(string text, out double value)
        {
            const NumberStyles styles = NumberStyles.Float;
            bool parsed = double.TryParse(text, styles, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, styles, CultureInfo.CurrentCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}
