using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Providers.TV;
using MediaBrowser.Providers.TV.TheTVDB;
using Microsoft.Extensions.Logging;
using TvDbSharper;

namespace MediaBrowser.Providers.People
{
    public class TvdbPersonImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly TvDbClientManager _tvDbClientManager;

        public TvdbPersonImageProvider(ILibraryManager libraryManager, IHttpClient httpClient, ILogger logger)
        {
            _libraryManager = libraryManager;
            _httpClient = httpClient;
            _logger = logger;
            _tvDbClientManager = TvDbClientManager.Instance;
        }

        public string Name => ProviderName;

        public static string ProviderName => "TheTVDB";

        public bool Supports(BaseItem item)
        {
            return item is Person;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary
            };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var seriesWithPerson = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Series).Name },
                PersonIds = new[] { item.Id },
                DtoOptions = new DtoOptions(false)
                {
                    EnableImages = false
                }

            }).Cast<Series>()
                .Where(i => TvdbSeriesProvider.IsValidSeries(i.ProviderIds))
                .ToList();

            var infos = await Task.WhenAll(seriesWithPerson.Select(async i => await GetImageFromSeriesData(i, item.Name, cancellationToken))
                .Where(i => i != null)
                .Take(1));

            return infos;
        }

        private async Task<RemoteImageInfo> GetImageFromSeriesData(Series series, string personName, CancellationToken cancellationToken)
        {
            var tvdbId = Convert.ToInt32(series.GetProviderId(MetadataProviders.Tvdb));

            try
            {
                var actorsResult = await _tvDbClientManager.GetActorsAsync(tvdbId, cancellationToken);
                var actor = actorsResult.Data.FirstOrDefault(a =>
                    string.Equals(a.Name, personName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(a.Image));
                if (actor == null)
                {
                    return null;
                }

                return new RemoteImageInfo
                {
                    Url = TVUtils.BannerUrl + actor.Image,
                    Type = ImageType.Primary,
                    ProviderName = Name
                };
            }
            catch (TvDbServerException e)
            {
                _logger.LogError(e, "Failed to retrieve actor {ActorName} from series {SeriesTvdbId}", personName, tvdbId);
                return null;
            }
        }

        public int Order => 1;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }
    }
}
