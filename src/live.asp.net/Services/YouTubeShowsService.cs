﻿// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using live.asp.net.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace live.asp.net.Services
{
    public class YouTubeShowsService : IShowsService
    {
        public const string CacheKey = nameof(YouTubeShowsService);

        private readonly IHostingEnvironment _env;
        private readonly AppSettings _appSettings;
        private readonly IMemoryCache _cache;

        public YouTubeShowsService(
            IHostingEnvironment env,
            IOptions<AppSettings> appSettings,
            IMemoryCache memoryCache)
        {
            _env = env;
            _appSettings = appSettings.Value;
            _cache = memoryCache;
        }

        public async Task<ShowList> GetRecordedShowsAsync(ClaimsPrincipal user, bool disableCache)
        {
            if (string.IsNullOrEmpty(_appSettings.YouTubeApiKey))
            {
                return new ShowList { PreviousShows = DesignData.Shows };
            }

            if (user.Identity.IsAuthenticated && disableCache)
            {
                return await GetShowsList();
            }

            var result = _cache.Get<ShowList>(CacheKey);

            if (result == null)
            {
                result = await GetShowsList();

                _cache.Set(CacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                });
            }

            return result;
        }

        private async Task<ShowList> GetShowsList()
        {
            var videoCount = 5 * 3; // 5 rows of 3 episodes

            using (var client = new YouTubeService(new BaseClientService.Initializer
            {
                ApplicationName = _appSettings.YouTubeApplicationName,
                ApiKey = _appSettings.YouTubeApiKey
            }))
            {
                // Get playlist items
                var listRequest = client.PlaylistItems.List("snippet");
                listRequest.PlaylistId = _appSettings.YouTubePlaylistId;
                listRequest.MaxResults = videoCount;
                var listResponse = await listRequest.ExecuteAsync();

                // Get video details for items in the playlist
                var videoIds = string.Join(',', listResponse.Items.Select(i => i.Snippet.ResourceId.VideoId));
                var videosRequest = client.Videos.List("snippet,liveStreamingDetails");
                videosRequest.Id = videoIds;
                videosRequest.MaxResults = videoCount;
                var videosResponse = await videosRequest.ExecuteAsync();

                // Build list of shows from video details
                var showList = new ShowList();
                var index = 0;
                foreach (var video in videosResponse.Items)
                {
                    var show = new Show
                    {
                        Provider = "YouTube",
                        ProviderId = video.Id,
                        Title = GetUsefulBitsFromTitle(video.Snippet.Title),
                        Description = video.Snippet.Description,
                        ThumbnailUrl = video.Snippet.Thumbnails.High.Url,
                        Url = GetVideoUrl(video.Id, _appSettings.YouTubePlaylistId, listResponse.Items[index].Snippet.Position ?? 0)
                    };

                    if (string.Equals(video.Snippet.LiveBroadcastContent, "upcoming", StringComparison.OrdinalIgnoreCase))
                    {
                        show.ShowDate = DateTimeOffset.Parse(video.LiveStreamingDetails.ScheduledStartTimeRaw, null, DateTimeStyles.RoundtripKind);
                        showList.UpcomingShows.Add(show);
                    }
                    else
                    {
                        show.ShowDate = DateTimeOffset.Parse(video.LiveStreamingDetails.ActualStartTimeRaw, null, DateTimeStyles.RoundtripKind);
                        showList.PreviousShows.Add(show);
                    }

                    index++;
                }

                // If there are more shows in the playlist than the count requested, set the URL to the playlist to see them
                if (!string.IsNullOrEmpty(listResponse.NextPageToken))
                {
                    showList.MoreShowsUrl = GetPlaylistUrl(_appSettings.YouTubePlaylistId);
                }

                return showList;
            }
        }

        private static string GetUsefulBitsFromTitle(string title)
        {
            if (title.Count(c => c == '-') < 2)
            {
                return string.Empty;
            }

            var lastHyphen = title.LastIndexOf('-');
            if (lastHyphen >= 0)
            {
                var result = title.Substring(lastHyphen + 1);
                return result;
            }

            return string.Empty;
        }

        private static string GetVideoUrl(string id, string playlistId, long itemIndex)
        {
            var encodedId = UrlEncoder.Default.Encode(id);
            var encodedPlaylistId = UrlEncoder.Default.Encode(playlistId);
            var encodedItemIndex = UrlEncoder.Default.Encode(itemIndex.ToString());

            return $"https://www.youtube.com/watch?v={encodedId}&list={encodedPlaylistId}&index={encodedItemIndex}";
        }

        private static string GetPlaylistUrl(string playlistId)
        {
            var encodedPlaylistId = UrlEncoder.Default.Encode(playlistId);

            return $"https://www.youtube.com/playlist?list={encodedPlaylistId}";
        }

        private static class DesignData
        {
            private static readonly TimeSpan _pstOffset = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time").BaseUtcOffset;

            public static readonly string LiveShow = null;

            public static readonly IList<Show> Shows = new List<Show>
            {
                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 21, 9, 30, 0, _pstOffset),
                    Title = "ASP.NET Community Standup - July 21st 2015",
                    Provider = "YouTube",
                    ProviderId = "7O81CAjmOXk",
                    ThumbnailUrl = "https://img.youtube.com/vi/7O81CAjmOXk/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=7O81CAjmOXk&index=1&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 14, 15, 30, 0, _pstOffset),
                    Title = "ASP.NET Community Standup - July 14th 2015",
                    Provider = "YouTube",
                    ProviderId = "bFXseBPGAyQ",
                    ThumbnailUrl = "https://img.youtube.com/vi/bFXseBPGAyQ/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=bFXseBPGAyQ&index=2&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },

                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 7, 15, 30, 0, _pstOffset),
                    Title = "ASP.NET Community Standup - July 7th 2015",
                    Provider = "YouTube",
                    ProviderId = "APagQ1CIVGA",
                    ThumbnailUrl = "https://img.youtube.com/vi/APagQ1CIVGA/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=APagQ1CIVGA&index=3&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
                new Show
                {
                    ShowDate = DateTimeOffset.Now.AddDays(-28),
                    Title = "ASP.NET Community Standup - July 21st 2015",
                    Provider = "YouTube",
                    ProviderId = "7O81CAjmOXk",
                    ThumbnailUrl = "https://img.youtube.com/vi/7O81CAjmOXk/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=7O81CAjmOXk&index=1&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
            };
        }
    }
}
