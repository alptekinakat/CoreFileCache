# CoreFileCache

Session Distuributed File Cache Library for Net Core 3.1 Web Project

You can convert core5 or core6 if you want 

Simple use



```
public void ConfigureServices(IServiceCollection services)
{
 ...
  services.AddCoreFileCache("cache", 10);
 ...
}
```

Application path/cache/cache/{SESSIONID}.cache --> Session time , expired, sliding...

Application path/cache/meta/{SESSIONID}.meta --> Session data
