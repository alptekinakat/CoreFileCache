# CoreFileCache

Session Distuributed File Cache for Net Core 3.1 Web Project  

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
