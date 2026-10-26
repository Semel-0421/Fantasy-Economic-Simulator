using System;
using System.Collections;
using System.IO;
using FES.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace FES.Unity
{
    /// <summary>
    /// Opens a data folder under StreamingAssets on every platform. Where StreamingAssets is a plain
    /// folder (Windows, Editor) files are read directly; where it is a URL (inside the Android APK) the
    /// known <see cref="DataFiles"/> are fetched with UnityWebRequest into memory first.
    /// </summary>
    public static class StreamingAssetsData
    {
        /// <param name="forceWebRequest">Fetch through UnityWebRequest even from a plain folder (tests).</param>
        public static IEnumerator Load(string folder, Action<IDataSource> onLoaded, bool forceWebRequest = false)
        {
            string root = Path.Combine(Application.streamingAssetsPath, folder);
            bool isUrl = root.Contains("://");
            if (!isUrl && !forceWebRequest)
            {
                onLoaded(new FileDataSource(root));
                yield break;
            }

            string baseUrl = isUrl ? root.Replace('\\', '/') : new Uri(root).AbsoluteUri;
            var data = new InMemoryDataSource();
            foreach (var file in DataFiles.All)
            {
                using var request = UnityWebRequest.Get(baseUrl + "/" + file);
                var operation = request.SendWebRequest();
                while (!operation.isDone) yield return null; // also advances in Edit Mode, unlike yielding the operation
                if (request.result == UnityWebRequest.Result.Success)
                    data.Add(file, request.downloadHandler.text.TrimStart('﻿'));
                else // Optional files may be absent; a missing required file fails in GameDatabase.Load.
                    Debug.Log($"[FES] Data file '{file}' not loaded: {request.error}");
            }
            onLoaded(data);
        }
    }
}
