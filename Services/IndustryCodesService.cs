﻿using System.Net.Http.Headers;
using bransjekartlegging.Models;
using bransjekartlegging.Services.Interfaces;

namespace bransjekartlegging.Services
{
    public class IndustryCodesService : IIndustryCodesService
    {
        private const string ApiEndpoint = "https://data.ssb.no/api/klass/v1/classifications/6/codesAt?date=";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        private List<IndustryCode> _industryCodes = new();

        private DateTime _refreshAt = DateTime.MinValue;
        private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(12);

        public IndustryCodesService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<List<IndustryCode>> GetIndustryCodes()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_refreshAt < DateTime.Now)
                {
                    await RefreshCache();
                }

                return _industryCodes;
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task<List<IndustryCode>> SearchIndustryCodes(string searchString)
        {
            var allIndustryCodes = await GetIndustryCodes();
            var matching = allIndustryCodes.Where(x => x.Name.Contains(searchString, StringComparison.InvariantCultureIgnoreCase) 
                || x.ShortName.Contains(searchString, StringComparison.InvariantCultureIgnoreCase) 
                || x.PresentationName.Contains(searchString, StringComparison.InvariantCultureIgnoreCase)).ToList();

            var parents = new List<IndustryCode>();
            // Get all parents for matching codes
            foreach (var match in matching) {
                IndustryCode? current = match with {};
                do {
                    var parent = allIndustryCodes.FirstOrDefault(x => x.Code == current.ParentCode);
                    if (parent != null && int.Parse(parent.Level) > 1 && !parents.Contains(parent) && !matching.Contains(parent)) {
                        parents.Add(parent with {});
                    }
                    current = parent;
                } while (current != null && current.ParentCode != null);
            }

            matching.AddRange(parents);
            matching.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.Ordinal));

            return matching;
        }

        private async Task RefreshCache()
        {
            var httpClient = _httpClientFactory.CreateClient("ssb");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var result = await httpClient.GetFromJsonAsync<IndustryCodesResult>(ApiEndpoint + DateTime.Now.ToString("yyyy-MM-dd"));
            if (result == null)
            {
                throw new ArgumentNullException();
            }

            _industryCodes = result.IndustryCodes;
            _refreshAt = DateTime.Now + _cacheTtl;
        }
    }
}
