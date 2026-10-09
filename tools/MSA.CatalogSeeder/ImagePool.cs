using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;

namespace MSA.CatalogSeeder;

/// <summary>
/// 商品图不按商品生成，而是每个品类准备一小池图片（默认 8 张），50 万商品从池里随机挑 1–4 张引用同一批 URL：
/// 总共只有几百个文件，上传一次即可，浏览器缓存也能复用。
/// <list type="bullet">
/// <item>svg（默认）：本地按品类配色、图标和品类名画占位图，离线、零成本、几 KB 一张；</item>
/// <item>pexels：用免费的 Pexels API（需要自己申请 key）按品类英文关键词下载真实照片，可免费商用、无需署名。</item>
/// </list>
/// 存放位置：默认传到对象存储（与后台上传的商品图同一个公共桶，URL 规则一致）；指定 --image-dir 时写到本地目录。
/// </summary>
internal sealed class ImagePool(SeederOptions options, HttpClient http)
{
    public async Task<IReadOnlyDictionary<string, string[]>> PrepareAsync(IReadOnlyList<CategorySpec> specs, CancellationToken cancellationToken)
    {
        using var s3 = options.ImageDirectory is null ? CreateS3() : null;
        var result = new Dictionary<string, string[]>();
        foreach (var spec in specs)
        {
            var files = options.ImageSource == "pexels"
                ? await DownloadPexelsAsync(spec, cancellationToken)
                : Enumerable.Range(0, options.ImagesPerCategory).Select(index => Svg(spec, index)).ToArray();
            var urls = new List<string>();
            for (var index = 0; index < files.Length; index++)
            {
                var (bytes, extension, contentType) = files[index];
                var key = $"catalog/seed/{spec.Code.ToLowerInvariant()}/{index + 1:D2}.{extension}";
                urls.Add(s3 is null
                    ? await WriteLocalAsync(key, bytes, cancellationToken)
                    : await UploadAsync(s3, key, bytes, contentType, cancellationToken));
            }

            result[spec.Code] = urls.ToArray();
            Console.WriteLine($"  图片池 {spec.Name,-6} {urls.Count} 张");
        }

        return result;
    }

    private AmazonS3Client CreateS3()
    {
        if (string.IsNullOrWhiteSpace(options.ObjectStorageEndpoint) || string.IsNullOrWhiteSpace(options.ObjectStorageAccessKey))
        {
            throw new InvalidOperationException(
                "对象存储未配置（msa.env 的 INFRA_OSS_ENDPOINT / ACCESS_KEY / SECRET_KEY）；或者用 --image-dir 把图片写到本地目录。");
        }

        return new AmazonS3Client(options.ObjectStorageAccessKey, options.ObjectStorageSecretKey, new AmazonS3Config
        {
            ServiceURL = options.ObjectStorageEndpoint,
            ForcePathStyle = true
        });
    }

    private async Task<string> UploadAsync(AmazonS3Client s3, string key, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            // 同一 URL 的内容不会变，允许浏览器长期缓存。
            Headers = { CacheControl = "public, max-age=31536000, immutable" }
        }, cancellationToken);
        // 与后台 BFF 生成的公开地址同一规则：{endpoint}/{bucket}/{key}
        var endpoint = options.ObjectStorageEndpoint!.TrimEnd('/');
        return $"{endpoint}/{Uri.EscapeDataString(options.Bucket)}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";
    }

    private async Task<string> WriteLocalAsync(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.ImageDirectory!, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return $"{options.ImageBaseUrl.TrimEnd('/')}/{key}";
    }

    /// <summary>800×800 的占位图：品类色渐变背景 + 大图标 + 品类名。同一品类的几张换角度与色相，看起来不完全一样。</summary>
    internal static (byte[] Bytes, string Extension, string ContentType) Svg(CategorySpec spec, int index)
    {
        var hue = spec.Hue + (index % 4 - 1.5) * 8;
        var angle = index * 45 % 360;
        var circleX = 140 + index * 83 % 520;
        var circleY = 120 + index * 137 % 440;
        var svg = string.Create(CultureInfo.InvariantCulture, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="800" height="800" viewBox="0 0 800 800">
              <defs>
                <linearGradient id="g" gradientTransform="rotate({angle} .5 .5)">
                  <stop offset="0" stop-color="hsl({hue:0} 55% 93%)"/>
                  <stop offset="1" stop-color="hsl({hue + 28:0} 50% 80%)"/>
                </linearGradient>
              </defs>
              <rect width="800" height="800" fill="url(#g)"/>
              <circle cx="{circleX}" cy="{circleY}" r="210" fill="hsl({hue:0} 60% 98%)" opacity=".45"/>
              <circle cx="{800 - circleX}" cy="{800 - circleY}" r="120" fill="hsl({hue + 40:0} 45% 70%)" opacity=".18"/>
              <text x="400" y="440" text-anchor="middle" font-size="260" font-family="Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif">{spec.Glyph}</text>
              <text x="400" y="610" text-anchor="middle" font-size="54" font-weight="600" fill="hsl({hue:0} 35% 28%)" font-family="PingFang SC, Microsoft YaHei, Noto Sans CJK SC, sans-serif">{spec.Name}</text>
              <text x="400" y="668" text-anchor="middle" font-size="24" fill="hsl({hue:0} 20% 40%)" font-family="PingFang SC, Microsoft YaHei, Noto Sans CJK SC, sans-serif" letter-spacing="6">示例图片 · {index + 1:D2}</text>
            </svg>
            """);
        return (Encoding.UTF8.GetBytes(svg), "svg", "image/svg+xml");
    }

    private async Task<(byte[] Bytes, string Extension, string ContentType)[]> DownloadPexelsAsync(CategorySpec spec, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(spec.PhotoKeyword)}&per_page={options.ImagesPerCategory}&orientation=square");
        request.Headers.Authorization = new AuthenticationHeaderValue(options.PexelsApiKey!);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        var files = new List<(byte[], string, string)>();
        foreach (var photo in document.RootElement.GetProperty("photos").EnumerateArray())
        {
            // large：宽 940 左右，列表卡片和详情页都够用。
            var url = photo.GetProperty("src").GetProperty("large").GetString()!;
            files.Add((await http.GetByteArrayAsync(url, cancellationToken), "jpg", "image/jpeg"));
        }

        // 某个关键词搜不到图时退回占位图，不让整个导入失败。
        return files.Count > 0 ? files.ToArray() : [Svg(spec, 0)];
    }
}
