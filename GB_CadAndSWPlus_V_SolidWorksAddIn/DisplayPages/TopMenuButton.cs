using System.ComponentModel; // 引入 Description 特性，用于设置命令的中文说明。
using Xarial.XCad.Base.Attributes; // 引入 XCAD 的命令标题和图标特性。

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>SolidWorks 顶部唯一命令组，统一显示为平台名称。</summary>
    [Title("CAD\\SolidWorks 数据互联平台")] // 设置 XCAD 命令组标题，覆盖默认的 TopMenuButton 名称。
    public enum TopMenuButton // 保留现有命令组类型，避免改变已有命令标识。
    {
        [Title("登录")] // 设置登录命令在顶部命令组中的中文名称。
        [Description("打开平台登录窗口")] // 设置登录命令的中文说明。
        [Icon(typeof(Resource), nameof(Resource.登录进入))] // 设置登录命令使用的图标资源。
        LogoIN, // 定义登录命令标识。
        [Title("退出")] // 设置退出命令在顶部命令组中的中文名称。
        [Description("退出当前平台账号")] // 设置退出命令的中文说明。
        [Icon(typeof(Resource), nameof(Resource.退出登录))] // 设置退出命令使用的图标资源。
        Logout, // 定义退出命令标识。
    }
}
