using System;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 仅用于调用方已验证的受控根装配体及三个完全解析、同配置、单位变换的组件。
    /// 实机对照及副作用验证尚未完成，complete 始终为 false，不表示工程上无干涉。
    /// ToolsCheckInterference2 为 void；其副作用、遗漏及 .NET 空值语义均未验证。
    /// 必须在实机上与原生命令及故意穿透样本对照后，才能评价接口可靠性。
    /// 预期的读取、身份或上下文错误以 InvalidOperationException 抛出，内部异常保留原始原因。
    /// </summary>
    internal static class PipelineThreePieceInterferenceVerifier
    {
        private const string Limit =
            "仅核验受控根装配体的三个组件，逐对调用 void 接口，coincidentInterference=false；" +
            "接口未提供成功状态，其副作用、遗漏及空值语义尚未验证。" +
            "非空结果仅表示可能存在干涉，不能确认体积干涉，也不能将重合面当作体积干涉；" +
            "空数组仅表示接口未报告，不证明工程上无干涉。" +
            "须在实机与原生命令及故意穿透样本对照后才能评价接口可靠性。";

        internal static string Verify(SldWorks app, AssemblyDoc assembly, Component2[] components,
            Action guard, out bool complete)
        {
            complete = false;
            var report = new StringBuilder(Limit);
            string stage = "输入检查";
            SolidWorksFileLogger.Info("三件干涉核验开始；" + Limit);
            try
            {
                if (app == null || assembly == null || guard == null || components == null ||
                    components.Length != 3 || components[0] == null || components[1] == null || components[2] == null)
                    throw new InvalidOperationException("输入不完整，必须提供三个组件及上下文检查。");

                // 复制组件引用，避免外部数组在检查过程中被替换。
                var requested = (Component2[])components.Clone();
                guard();
                for (int i = 0; i < 3; i++)
                    for (int j = i + 1; j < 3; j++)
                        if (Same(app, requested[i], requested[j], guard))
                            throw new InvalidOperationException("输入组件身份重复。");

                for (int i = 0; i < 3; i++)
                {
                    for (int j = i + 1; j < 3; j++)
                    {
                        stage = "组件对 " + (i + 1) + "-" + (j + 1);
                        SolidWorksFileLogger.Info(stage + "：开始调用干涉接口；只传当前两件。");
                        object? componentResult = null;
                        object? faceResult = null;
                        Call(guard, delegate
                        {
                            // 与官方示例保持组件数组类型；该接口仍须隔离实机验证，入口保持暂停。
                            assembly.ToolsCheckInterference2(2, new Component2[] { requested[i], requested[j] },
                                false, out componentResult, out faceResult);
                            return 0;
                        });
                        string types = "组件输出=" + Describe(componentResult) +
                            "；面输出=" + Describe(faceResult);
                        SolidWorksFileLogger.Info(stage + "：" + types + "。");
                        report.Append(" ").Append(stage).Append("：").Append(types).Append("；");

                        if (HasItems(componentResult) || HasItems(faceResult))
                        {
                            report.Append("接口返回非空数组，可能存在干涉；");
                            SolidWorksFileLogger.Warning(stage + "：输出非空，可能存在干涉；尚需核对数据。");
                        }

                        Array returnedComponents = ReadArray(componentResult, "组件输出", typeof(Component2));
                        Array returnedFaces = ReadArray(faceResult, "面输出", typeof(Face2));
                        report.Append("组件数=").Append(returnedComponents.Length)
                            .Append("，面数=").Append(returnedFaces.Length).Append("；");
                        bool[] present = new bool[2];
                        bool[] facePresent = new bool[2];
                        Component2[] pair = { requested[i], requested[j] };
                        foreach (object value in returnedComponents)
                        {
                            var component = value as Component2;
                            if (component == null) throw new InvalidOperationException("组件输出含空值或非组件。");
                            int index = Match(app, component, pair, guard);
                            present[index] = true;
                        }
                        foreach (object value in returnedFaces)
                        {
                            var face = value as Face2;
                            var entity = value as Entity;
                            if (face == null || entity == null)
                                throw new InvalidOperationException("面输出含空值或无法读取面实体。");
                            object owner = Call(guard, delegate { return entity.GetComponent(); });
                            var ownerComponent = owner as Component2;
                            if (ownerComponent == null) throw new InvalidOperationException("无法读取面所属组件。");
                            int index = Match(app, ownerComponent, pair, guard);
                            if (!present[index]) throw new InvalidOperationException("面所属组件未出现在组件输出中。");
                            facePresent[index] = true;
                        }

                        if (returnedComponents.Length == 0 && returnedFaces.Length == 0)
                        {
                            report.Append("接口返回两个空数组，接口未报告。");
                            SolidWorksFileLogger.Info(stage + "：两个空数组；接口未报告，不能推断工程上无干涉。");
                        }
                        else if (returnedFaces.Length == 0 && present[0] && present[1])
                        {
                            report.Append("接口返回两件组件但没有干涉面；可能为接触或无法判定，继续检查其他组件对。");
                            SolidWorksFileLogger.Warning(stage + "：返回两件组件但没有干涉面，不能判定体积干涉。");
                        }
                        else
                        {
                            if (!present[0] || !present[1] || !facePresent[0] || !facePresent[1])
                                throw new InvalidOperationException("非空输出未能同时关联两件组件及其面，结果不完整。");
                            report.Append("已核对组件及面所属身份，不能确认体积干涉。");
                            SolidWorksFileLogger.Warning(stage + "：输出非空，可能存在干涉；不能确认体积干涉。");
                        }
                    }
                }
                stage = "最终上下文检查";
                guard();
                report.Append(" 三对输出已读取并进行形状及身份检查；接口试验未完成，complete=false。");
                SolidWorksFileLogger.Warning("三件干涉核验：三对输出已读取；接口可靠性和副作用仍未验证，试验未完成。");
            }
            catch (COMException ex) { throw Failure(stage, report, ex); }
            catch (InvalidComObjectException ex) { throw Failure(stage, report, ex); }
            catch (InvalidOperationException ex) { throw Failure(stage, report, ex); }
            catch (ArgumentException ex) { throw Failure(stage, report, ex); }
            catch (NotSupportedException ex) { throw Failure(stage, report, ex); }
            catch (ArithmeticException ex) { throw Failure(stage, report, ex); }
            catch (System.IO.IOException ex) { throw Failure(stage, report, ex); }
            catch (UnauthorizedAccessException ex) { throw Failure(stage, report, ex); }
            catch (System.Security.SecurityException ex) { throw Failure(stage, report, ex); }
            catch (AggregateException ex) { throw Failure(stage, report, ex); }
            return report.ToString();
        }

        private static InvalidOperationException Failure(string stage, StringBuilder report, Exception cause)
        {
            // 日志只记录类型，不记录外部异常消息；原始异常链保留在 InnerException。
            SolidWorksFileLogger.Error("三件干涉核验未完成；阶段=" + stage +
                "；异常类型=" + cause.GetType().Name + "；不能推断无干涉。");
            string detail = cause is InvalidOperationException || cause is ArgumentException
                ? cause.Message : "请查看详细日志。";
            return new InvalidOperationException(report + " 核验未完成；阶段=" + stage +
                "；异常类型=" + cause.GetType().Name + "；原因=" + detail +
                "。未检查或无法判读的结果不得推断为无干涉。", cause);
        }

        private static string Describe(object? value)
        {
            if (value == null) return "null";
            string type = value.GetType().FullName ?? value.GetType().Name;
            var array = value as Array;
            if (array == null) return "类型=" + type;
            return "类型=" + type + "，维数=" + array.Rank + "，长度=" + array.Length +
                (array.Rank == 1 ? "，下标起点=" + array.GetLowerBound(0) : "");
        }

        private static bool HasItems(object? value)
        {
            var array = value as Array;
            return array != null && array.Length != 0;
        }

        private static Array ReadArray(object? value, string description, Type expectedType)
        {
            if (value == null)
                throw new InvalidOperationException(description + "为 null；文档语义不足，无法判定。");
            var array = value as Array;
            if (array == null || array.Rank != 1)
                throw new InvalidOperationException(description + "不是可判读的一维数组。");
            Type? elementType = array.GetType().GetElementType();
            if (elementType == null || (elementType != typeof(object) && !expectedType.IsAssignableFrom(elementType)))
                throw new InvalidOperationException(description + "的数组元素类型无法判读。");
            // foreach 支持非零下标的 SAFEARRAY；null 和错误码均不可当作空数组。
            return array;
        }

        private static int Match(SldWorks app, object value, Component2[] pair, Action guard)
        {
            bool first = Same(app, value, pair[0], guard);
            bool second = Same(app, value, pair[1], guard);
            if (first == second) throw new InvalidOperationException("输出身份不属于唯一请求组件。");
            return first ? 0 : 1;
        }

        private static bool Same(SldWorks app, object left, object right, Action guard)
        {
            int result = Call(guard, delegate { return app.IsSame(left, right); });
            if (result == (int)swObjectEquality.swObjectSame) return true;
            if (result == (int)swObjectEquality.swObjectNotSame) return false;
            throw new InvalidOperationException("组件身份比较结果无法判定。");
        }

        // 对每个 COM 调用执行前后检查；同时保留调用及后检查的异常。
        private static T Call<T>(Action guard, Func<T> operation)
        {
            guard();
            Exception? failure = null;
            try { return operation(); }
            catch (COMException ex) { failure = ex; throw; }
            catch (InvalidComObjectException ex) { failure = ex; throw; }
            catch (InvalidOperationException ex) { failure = ex; throw; }
            catch (ArgumentException ex) { failure = ex; throw; }
            catch (NotSupportedException ex) { failure = ex; throw; }
            catch (ArithmeticException ex) { failure = ex; throw; }
            catch (System.IO.IOException ex) { failure = ex; throw; }
            catch (UnauthorizedAccessException ex) { failure = ex; throw; }
            catch (System.Security.SecurityException ex) { failure = ex; throw; }
            catch (AggregateException ex) { failure = ex; throw; }
            finally
            {
                try { guard(); }
                catch (COMException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (InvalidComObjectException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (InvalidOperationException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (ArgumentException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (NotSupportedException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (ArithmeticException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (System.IO.IOException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (UnauthorizedAccessException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (System.Security.SecurityException post) { if (failure != null) throw Combined(failure, post); throw; }
                catch (AggregateException post) { if (failure != null) throw Combined(failure, post); throw; }
            }
        }

        private static AggregateException Combined(Exception operation, Exception postGuard)
        {
            return new AggregateException("COM 调用与后置上下文检查均失败，两个内部异常分别保留原始原因。",
                operation, postGuard);
        }
    }
}
