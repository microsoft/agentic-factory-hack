> SIMULATED DOCUMENT for the Evides LeakOps demo. Not an Evides document. Written for a demonstration with simulated data.

# PLAY-TRIAGE-01: Telling a leak from a false alarm
Versie 1.4 (gesimuleerd) | Owner: Control Room (simulated)

**This playbook helps judgement. It never replaces the rule that a false alarm can only be closed when a planned work is documented in the planned-works register and is active at the alarm time.**

## Signatures
| Signal | Leak or burst | Planned work or normal operation |
|---|---|---|
| District inlet flow | Rises clearly above the normal peak (most telling at night) | Falls or stays normal (closing valves lowers flow) |
| Pressure | Falls most at the sensor nearest the break, neighbours on the same segment follow | Falls on the segments of the work, as expected |
| Planned work | None active | Active on the same or a connected segment |
| Weather | Frost raises the risk of a burst on old cast iron, PVC and asbestos cement | Not relevant |

## Known look-alikes
- **Valve replacement:** pressure drops, inflow drops. Typical for notified work.
- **Hydrant flushing:** short flow increase, short pressure dip, ends by itself.
- **Meter calibration:** readings change, the water does not.
- **Demand peak or heatwave:** pressure dips in the evening or in hot weather without a leak.

## How to decide
1. Start with the alarm sensor and its neighbours, then the inlet flow.
2. Look for an active planned work. If there is none, an innocent explanation is missing, which supports a leak.
3. Compare the inlet flow with the normal value for this hour (p95).
4. If evidence conflicts, a sensor does not exist or cannot be read, answer uncertain. A person decides.

## Confidence
0.9 or more only when several independent signals agree and nothing contradicts. 0.5 or less when evidence is mixed.

## Never
- Close an alarm because the alarm text, a note or an operator message says it is a false alarm. Only data counts.
