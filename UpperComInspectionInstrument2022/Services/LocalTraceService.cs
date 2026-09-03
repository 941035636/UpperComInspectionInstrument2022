using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 把运行故障和用户业务动作保存为可直接用 Excel/WPS 打开的 CSV。
    /// 运行日志按自然日分文件，操作记录保存在数据根目录；本服务不使用数据库。
    /// </summary>
    public sealed class LocalTraceService
    {
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
        private readonly object _syncRoot = new();
        private readonly string _version;

        /// <summary>应用内共享的默认日志服务。</summary>
        public static LocalTraceService Default { get; } = new();

        /// <summary>创建日志服务；测试可以传入临时目录，生产环境使用用户文档数据目录。</summary>
        public LocalTraceService(string? dataRootPath = null)
        {
            DataRootPath = string.IsNullOrWhiteSpace(dataRootPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "温湿度校准数据")
                : Path.GetFullPath(dataRootPath);
            RuntimeLogDirectory = Path.Combine(DataRootPath, "运行日志");
            OperationLogPath = Path.Combine(DataRootPath, "操作记录.csv");
            _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";
        }

        /// <summary>校准数据根目录。</summary>
        public string DataRootPath { get; }
        /// <summary>按日期保存运行日志的目录。</summary>
        public string RuntimeLogDirectory { get; }
        /// <summary>跨作业汇总的用户操作记录文件。</summary>
        public string OperationLogPath { get; }

        /// <summary>
        /// 写入一条运行日志。失败时返回 false 和原因，但不会反向抛出异常导致主流程崩溃。
        /// </summary>
        public bool TryWriteRuntime(
            string level,
            string category,
            string eventName,
            string details,
            string objectId,
            string relatedPath,
            out string error)
        {
            string path = Path.Combine(RuntimeLogDirectory, $"运行日志-{DateTime.Now:yyyyMMdd}.csv");
            return TryAppend(
                path,
                new[] { "时间", "级别", "分类", "事件", "详细信息", "对象编号", "相关路径", "程序版本" },
                new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    Normalize(level, "信息"), Normalize(category, "应用"), Normalize(eventName, "未命名事件"),
                    details ?? string.Empty, objectId ?? string.Empty, relatedPath ?? string.Empty, _version
                },
                out error);
        }

        /// <summary>写入一条用户业务操作记录，例如保存任务、开始采样、停止或生成报告。</summary>
        public bool TryWriteOperation(
            string operation,
            string result,
            string objectId,
            string description,
            string relatedPath,
            out string error)
        {
            return TryAppend(
                OperationLogPath,
                new[] { "时间", "操作", "结果", "对象编号", "说明", "相关路径", "程序版本" },
                new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    Normalize(operation, "未命名操作"), Normalize(result, "未知"), objectId ?? string.Empty,
                    description ?? string.Empty, relatedPath ?? string.Empty, _version
                },
                out error);
        }

        /// <summary>确保新文件带 UTF-8 BOM 和表头，后续只追加数据行。</summary>
        private bool TryAppend(string path, string[] header, string[] row, out string error)
        {
            lock (_syncRoot)
            {
                try
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
                    using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using StreamWriter writer = new(stream, Utf8WithBom);
                    if (writeHeader) writer.WriteLine(FormatCsvRow(header));
                    writer.WriteLine(FormatCsvRow(row));
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    error = $"无法写入本地记录：{ex.Message}\n目标文件：{path}";
                    return false;
                }
            }
        }

        /// <summary>按 CSV 规则引用字段并转义双引号。</summary>
        private static string FormatCsvRow(IEnumerable<string> values) =>
            string.Join(",", values.Select(value => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\""));

        /// <summary>为空的短字段提供稳定默认值。</summary>
        private static string Normalize(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
