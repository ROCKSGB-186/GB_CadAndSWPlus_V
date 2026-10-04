namespace GB_CadAndSWPlus_V.Shared.Services
{
    /// <summary>
    /// 统一维护各项目的发布版本，避免 net472 客户端直接引用 net8 服务端程序集。
    /// </summary>
    public static class ProjectVersionInfo
    {
        public const string UnifiedVersion = "3.26.1004.169";
        public const string CadVersion = UnifiedVersion;
        public const string SolidWorksAddInVersion = UnifiedVersion;
        public const string SharedVersion = UnifiedVersion;
        public const string ServerVersion = UnifiedVersion;
        public const string TrayVersion = UnifiedVersion;
    }
}
