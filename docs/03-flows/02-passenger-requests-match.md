## Overview

There are 4 approaches, but those have 2 unrelated aspects:
First aspect is how the routes are chosen:
1. All accepatble routes are SENT to Passenger, Passenger then selects 1 and that one is processed
2. All acceptable routes are NOT SEND to the Passenger, instead they are sent async to Drivers of those Routes, first Driver to accept gets the Route
Second aspect is how the routes are modified to accomodate the Trip:
1. Driver's Route is NOT MODIFIED, Passenger is moved to some [Ride Screen] and the coordination of Driver and Passenger is ceded to them via Chat Service and other tools
2. Driver's Route is MODIFIED, Driver has to accept the modification of the Route that may be changed to fit Passenger's destination.

Each one of them is TODO, in files:
- [./02-1.md]
- [./02-2.md]
- [./02-3.md]
- [./02-4.md]
