using System.IO;
using UnityEngine;
using work.ctrl3d;

public class GameManager : MonoBehaviour
{
    private void Start()
    {
        var path = Path.Combine(Application.dataPath, "RuntimeConfig.json");
        
        var runtimeConfig = new JsonConfig<RuntimeConfig>(path);
        runtimeConfig.ConfigChanged += OnConfigChanged;
        runtimeConfig.Load();
        
        /*
        if (result.IsSuccess)
        {
            result.Data.Name = "Test";
            result.Data.Value = 1000;
            simpleConfig.SetConfig(result.Data);
        }
        else
        {
            Debug.Log("!!" + result.ErrorMessage);
        }
        */

        //simpleConfig.Save();
    }

    private void OnConfigChanged(RuntimeConfig data)
    {
        Debug.Log(data.application.runInBackground);
        
        //Debug.Log("######"+ data.Name);
    }
}
