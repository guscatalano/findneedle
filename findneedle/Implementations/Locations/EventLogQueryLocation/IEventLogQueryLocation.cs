﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Locations.EventLogQueryLocation;
public abstract class IEventLogQueryLocation : SearchLocation
{
    public override void SetNotificationCallback(SearchProgressSink sink)
    {

    }
    public override void SetSearchStatistics(SearchStatistics stats)
    {
    }
}