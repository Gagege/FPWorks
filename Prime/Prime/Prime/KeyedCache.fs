﻿namespace Prime

[<RequireQualifiedAccess>]
module KeyedCacheMetrics =
    let mutable internal GlobalCacheHits = 0L
    let mutable internal GlobalCacheMisses = 0L
    let getGlobalCacheHits () = GlobalCacheHits
    let getGlobalCacheMisses () = GlobalCacheMisses

/// Presents a purely-functional interface to a cached value.
/// TODO: document this type's functions!
type [<ReferenceEquality>] KeyedCache<'k, 'v when 'k : equality> =
    private
        { mutable CacheKey : 'k
          mutable CacheValue : 'v }

    static member make (cacheKey : 'k) (cacheValue : 'v) =
        { CacheKey = cacheKey
          CacheValue = cacheValue }

    static member getValue (keyEquality : 'k -> 'k -> bool) getFreshKeyAndValue cacheKey keyedCache : 'v =
        if not <| keyEquality keyedCache.CacheKey cacheKey then
#if DEBUG
            KeyedCacheMetrics.GlobalCacheMisses <- KeyedCacheMetrics.GlobalCacheMisses + 1L
#endif
            let (freshKey, freshValue) = getFreshKeyAndValue ()
            keyedCache.CacheKey <- freshKey
            keyedCache.CacheValue <- freshValue
            freshValue
        else
#if DEBUG
            KeyedCacheMetrics.GlobalCacheHits <- KeyedCacheMetrics.GlobalCacheHits + 1L
#endif
            keyedCache.CacheValue
