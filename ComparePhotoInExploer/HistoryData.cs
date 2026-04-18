using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ComparePhotoInExploer;

/// <summary>
/// 一组对比历史记录
/// </summary>
public class HistoryGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public List<string> ImagePaths { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 历史数据持久化管理
/// 缩略图以图片路径哈希为键存储，同一图片在不同组中不重复生成
/// </summary>
public static class HistoryData
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "Local", "ComparePhotoInExploer");

    private static readonly string ThumbsDir = Path.Combine(AppDataDir, "Thumbs");
    private static readonly string DataFile = Path.Combine(AppDataDir, "history.json");
    private const int MaxGroups = 10;

    public static string GetThumbsDir() => ThumbsDir;

    /// <summary>
    /// 根据图片文件路径生成稳定的哈希文件名
    /// </summary>
    public static string GetPathHash(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return Convert.ToHexString(bytes)[..12]; // 取前12字符，足够唯一
    }

    /// <summary>
    /// 获取缩略图路径（以图片路径哈希为键）
    /// </summary>
    public static string GetThumbnailPath(string pathHash)
    {
        return Path.Combine(ThumbsDir, $"{pathHash}.png");
    }

    /// <summary>
    /// 检查缩略图是否已存在
    /// </summary>
    public static bool ThumbnailExists(string pathHash)
    {
        return File.Exists(GetThumbnailPath(pathHash));
    }

    /// <summary>
    /// 加载所有历史组
    /// </summary>
    public static List<HistoryGroup> Load()
    {
        try
        {
            if (!File.Exists(DataFile)) return new List<HistoryGroup>();
            var json = File.ReadAllText(DataFile);
            var result = JsonSerializer.Deserialize<List<HistoryGroup>>(json);
            return result ?? new List<HistoryGroup>();
        }
        catch
        {
            return new List<HistoryGroup>();
        }
    }

    /// <summary>
    /// 保存所有历史组
    /// </summary>
    public static void Save(List<HistoryGroup> groups)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataFile, json);
        }
        catch
        {
            // 忽略保存失败
        }
    }

    /// <summary>
    /// 保存缩略图（以路径哈希为键）
    /// </summary>
    public static void SaveThumbnail(string pathHash, Image thumbnail)
    {
        try
        {
            Directory.CreateDirectory(ThumbsDir);
            thumbnail.Save(GetThumbnailPath(pathHash), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch
        {
            // 忽略保存失败
        }
    }

    /// <summary>
    /// 加载缩略图（以路径哈希为键，复制到内存避免文件锁）
    /// </summary>
    public static Image? LoadThumbnail(string pathHash)
    {
        try
        {
            var path = GetThumbnailPath(pathHash);
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var rawImg = Image.FromStream(fs);
            var bmp = new Bitmap(rawImg);
            rawImg.Dispose();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 删除某组引用的所有缩略图（仅删除不被其他组共享的）
    /// </summary>
    public static void DeleteGroupThumbnails(List<HistoryGroup> allGroups, string groupId)
    {
        try
        {
            var target = allGroups.FirstOrDefault(g => g.Id == groupId);
            if (target == null) return;

            // 收集其他组使用的路径哈希
            var otherHashes = new HashSet<string>();
            foreach (var g in allGroups)
            {
                if (g.Id == groupId) continue;
                foreach (var p in g.ImagePaths)
                    otherHashes.Add(GetPathHash(p));
            }

            // 只删除不被其他组共享的缩略图
            foreach (var p in target.ImagePaths)
            {
                var hash = GetPathHash(p);
                if (!otherHashes.Contains(hash))
                {
                    var path = GetThumbnailPath(hash);
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 添加或更新历史组，返回更新后的列表。处理去重、排序、上限逻辑。
    /// </summary>
    public static List<HistoryGroup> AddOrUpdateGroup(List<HistoryGroup> existing, string[] imagePaths)
    {
        var pathList = imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

        // 检查是否有相同的组（路径集合相同，忽略顺序）
        var pathSet = new HashSet<string>(pathList);
        int dupIdx = -1;
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].ImagePaths.Count == pathList.Count &&
                existing[i].ImagePaths.All(p => pathSet.Contains(p)))
            {
                dupIdx = i;
                break;
            }
        }

        if (dupIdx >= 0)
        {
            // 重复组提到最前，其余顺延
            var dup = existing[dupIdx];
            existing.RemoveAt(dupIdx);
            existing.Insert(0, dup);
        }
        else
        {
            // 新组插入最前
            var newGroup = new HistoryGroup { ImagePaths = pathList, CreatedAt = DateTime.Now };
            existing.Insert(0, newGroup);

            // 超出上限则移除最后一组及其缩略图
            while (existing.Count > MaxGroups)
            {
                var removed = existing[existing.Count - 1];
                DeleteGroupThumbnails(existing, removed.Id);
                existing.RemoveAt(existing.Count - 1);
            }
        }

        return existing;
    }
}
