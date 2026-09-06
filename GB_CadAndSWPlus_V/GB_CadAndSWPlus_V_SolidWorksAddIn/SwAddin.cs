using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using System;
using System.Runtime.InteropServices;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn
{
    /// <summary>
    /// GB 工程平台 SolidWorks 插件的主类，实现了 ISwAddin 接口，用于与 SolidWorks 进行交互。
    /// </summary>
    [ComVisible(true)]
    [Guid(AddInGuid)]
    [ProgId("GB_CadAndSWPlus_V.SolidWorksAddIn")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class SwAddin : ISwAddin
    {
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的唯一标识符，用于在注册表中注册插件信息。
        /// </summary>
        public const string AddInGuid = "2B3F8C3D-9D7A-4E89-9E3B-0D5B3C5C1C42";
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的标题，用于在 SolidWorks 插件管理器中显示。
        /// </summary>
        private const string AddInTitle = "GB 工程平台";
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的描述，用于在 SolidWorks 插件管理器中显示。
        /// </summary>
        private const string AddInDescription = "GB CAD/P&ID 与 SolidWorks 工程数据连接插件";
        /// <summary>
        /// 存储 SolidWorks 应用程序对象的引用，用于与 SolidWorks 进行交互。
        /// </summary>
        private SldWorks? _solidWorks;
        /// <summary>
        /// 存储 SolidWorks 插件的 Cookie，用于标识插件在 SolidWorks 中的唯一实例。
        /// </summary>
        private int _cookie;
        /// <summary>
        /// 连接到 SolidWorks 应用程序，并获取插件的 Cookie，用于标识插件在 SolidWorks 中的唯一实例。
        /// </summary>
        /// <param name="ThisSW">SolidWorks 应用程序对象。</param>
        /// <param name="Cookie">插件的唯一标识符。</param>
        /// <returns>如果成功连接到 SolidWorks，则返回 true；否则返回 false。</returns>
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            if (ThisSW == null)
            {
                return false;
            }

            try
            {
                _solidWorks = ThisSW as SldWorks;
                if (_solidWorks == null)
                {
                    return false;
                }

                _cookie = Cookie;
                return true;
            }
            catch
            {
                _solidWorks = null;
                _cookie = 0;
                return false;
            }
        }

        /// <summary>
        /// 断开与 SolidWorks 应用程序的连接，并清除插件的 Cookie。
        /// </summary>
        /// <returns>如果成功断开连接，则返回 true；否则返回 false。</returns>
        public bool DisconnectFromSW()
        {
            _solidWorks = null;
            _cookie = 0;
            return true;
        }

        /// <summary>
        /// 在注册表中注册 SolidWorks 插件信息。
        /// </summary>
        /// <param name="type">要注册的插件类型。</param>
        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            string addInKeyPath = $"SOFTWARE\\SolidWorks\\AddIns\\{{{AddInGuid}}}";
            using (RegistryKey? addInKey = Registry.LocalMachine.CreateSubKey(addInKeyPath))
            {
                if (addInKey == null)
                {
                    throw new InvalidOperationException("无法创建 SolidWorks Add-in 注册表项，请使用管理员权限注册插件。");
                }

                addInKey.SetValue(null, 1, RegistryValueKind.DWord);
                addInKey.SetValue("Title", AddInTitle, RegistryValueKind.String);
                addInKey.SetValue("Description", AddInDescription, RegistryValueKind.String);
            }

            using (RegistryKey? startupKey = Registry.CurrentUser.CreateSubKey(
                $"Software\\SolidWorks\\AddInsStartup\\{{{AddInGuid}}}"))
            {
                startupKey?.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }
        /// <summary>
        /// 在注册表中注销 SolidWorks 插件信息。
        /// </summary>
        /// <param name="type">要注销的插件类型。</param>
        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            Registry.LocalMachine.DeleteSubKeyTree(
                $"SOFTWARE\\SolidWorks\\AddIns\\{{{AddInGuid}}}", false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $"Software\\SolidWorks\\AddInsStartup\\{{{AddInGuid}}}", false);
        }
    }
}
