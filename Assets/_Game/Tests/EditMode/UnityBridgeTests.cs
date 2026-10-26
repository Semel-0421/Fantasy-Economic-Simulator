using System.Collections;
using FES.Data;
using FES.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace FES.Tests
{
    public class UnityBridgeTests
    {
        [UnityTest]
        public IEnumerator StreamingAssets_WebRequestPath_LoadsSameDataAsFiles()
        {
            // The Android path (UnityWebRequest into memory), exercised against the local folder.
            IDataSource fetched = null;
            var load = StreamingAssetsData.Load("Data", loaded => fetched = loaded, forceWebRequest: true);
            while (load.MoveNext()) yield return load.Current;

            Assert.IsInstanceOf<InMemoryDataSource>(fetched);
            foreach (var file in DataFiles.All)
                Assert.IsTrue(fetched.Exists(file), file);
            Assert.AreEqual(GameDatabase.Load(TestWorlds.Sample()).ContentHash, GameDatabase.Load(fetched).ContentHash);
        }
    }
}
