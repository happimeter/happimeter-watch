﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Happimeter.Interfaces;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Diagnostics;

namespace Happimeter.Services
{
    public class RestService : IRestService
    {
        private HttpClient _httpClient;
        private string _authenticationHeaderValue = null;
        public RestService()
        {
            _httpClient = new HttpClient();
            //smaller than default value
            //_httpClient.MaxResponseContentBufferSize = 256000;
        }

        public RestService(string authToken) : this()
        {
            AddAuthorizationTokenToInstance(authToken);
        }

        public void AddAuthorizationTokenToInstance(string token)
        {
            _authenticationHeaderValue = $"Bearer {token}";
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<HttpResponseMessage> Get(string url)
        {
            return await _httpClient.GetAsync(url);
        }

        public async Task<HttpResponseMessage> Post(string url, object data)
        {
            var json = JsonConvert.SerializeObject(data);
            System.Diagnostics.Debug.WriteLine(json);
            var content = new StringContent(JsonConvert.SerializeObject(data), System.Text.Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(url, content);
        }

        public async Task<HttpResponseMessage> Delete(string url)
        {
            return await _httpClient.DeleteAsync(url);
        }

        public async Task<HttpResponseMessage> FileUpload(string url, string path)
        {
            var bytes = File.ReadAllBytes(path);
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            content.Add(fileContent, "upload");
            return await _httpClient.PostAsync(url, content);
        }
    }
}
