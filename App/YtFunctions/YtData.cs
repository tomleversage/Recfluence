﻿using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using SysExtensions.Serialization;
using SysExtensions.Threading;
using YtReader.Db;

namespace YtFunctions {
  public class YtData {
    /// <summary>Use the Json.net defaults because we want to keep original name casings so that we aren't re-casing the
    ///   databse in different formats</summary>
    static readonly JsonSerializerSettings JCfg = new JsonSerializerSettings {Formatting = Formatting.None};
    readonly AsyncLazy<FuncCtx, ExecutionContext> Ctx;

    public YtData(AsyncLazy<FuncCtx, ExecutionContext> ctx) => Ctx = ctx;

    [FunctionName("video")]
    public async Task<HttpResponseMessage> Video([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "video/{videoId}")]
      HttpRequest req, string videoId, ExecutionContext exec) =>
      await Ctx.Run(exec, async c => {
        var db = c.Scope.Resolve<AppDb>();
        using var conn = await db.OpenConnection();
        var videoTask = conn.QueryFirstOrDefaultAsync<DbVideo>("select * from video_latest where video_id = :video_id", new {video_id = videoId});
        var video = await videoTask;
        if (video == null) return new VideoResponse { error = $"Video {videoId} not found"}.JsonResponse(JCfg);
        var channel = await conn.QueryFirstAsync<DbChannel>("select * from channel_latest where channel_id= :channel_id", new {channel_id = video.CHANNEL_ID});
        channel.TAGS = channel.TAGS.Replace("\n", " ");
        var res = new VideoResponse {
          video = video,
          channel = channel
        };
        return res.JsonResponse(JCfg);
      });

    [FunctionName("captions")]
    public async Task<HttpResponseMessage> Captions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "captions/{videoId}")]
      HttpRequest req, string videoId, ExecutionContext exec) =>
      await Ctx.Run(exec, async c => {
        var db = c.Scope.Resolve<AppDb>();
        using var conn = await db.OpenConnection();
        var captions = await conn.QueryAsync<DbCaption>("select * from caption where video_id = :video_id order by offset_seconds", new {video_id = videoId});
        return captions.JsonResponse(JCfg);
      });

    public class VideoResponse {
      public DbVideo   video   { get; set; }
      public DbChannel channel { get; set; }
      public string    error   { get; set; }
    }
  }

  public static class HttpResponseEx {
    public static HttpResponseMessage JsonResponse(this object o, JsonSerializerSettings settings) =>
      new HttpResponseMessage(HttpStatusCode.OK) {
        Content = new StringContent(o.ToJson(settings), Encoding.UTF8, "application/json")
      };
  }
}