using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace configlib.source.Typed.Extensions;

public static class ApiExtensions
{
    public static void PushEventSafe(this IEventAPI eventAPI, ILogger logger, string eventName, IAttribute? attribute = null)
    {
        try
        {
            eventAPI.PushEvent(eventName, attribute);
        }
        catch(Exception ex)
        {
            logger.Error("Exception when pushing event '{0}', exception: {1}", eventName, ex);
        }
    }
}
