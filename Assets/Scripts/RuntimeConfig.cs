using System;

[Serializable]
public class RuntimeConfig
{
    public ApplicationConfig application = new();
    public ScreenConfig screen = new();
    public QualityConfig quality = new();
    public AudioConfig audio = new();
    public CameraConfig camera = new();
    public WindowConfig window = new();
}


[Serializable]
public class ApplicationConfig
{
    public bool runInBackground = true;
    public int targetFrameRate = 60;
}

[Serializable]
public class ScreenConfig
{
    public int x;
    public int y;
    public int width = 1280;
    public int height = 720;
    public bool fullScreen = false;
}

[Serializable]
public class QualityConfig
{
    public int vSyncCount = 0;
    public int qualityLevel = 5;
}

[Serializable]
public class AudioConfig
{
    public float volume = 1.0f;
}

[Serializable]
public class CameraConfig
{
    public string backgroundColor = "#000000";
    public float fieldOfView = 60.0f;
    public float nearClipPlane = 0.3f;
    public float farClipPlane = 1000.0f;
}

[Serializable]
public class WindowConfig
{
    public bool popupWindow;
    public string zOrder = "TOP";
}