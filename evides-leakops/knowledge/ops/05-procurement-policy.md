> SIMULATED DOCUMENT for the Evides LeakOps demo. Not an Evides document. Written for a demonstration with simulated data.

# POL-PROC-03: Spare parts and emergency purchasing policy
Versie 1.2 (gesimuleerd) | Owner: Procurement and Logistics (simulated)

## Reserve first, then order
1. Reserve what is in stock for the incident. Reservations are one per incident and SKU, so repeating a request never reserves twice.
2. Only order what is missing for the repair (**urgent order**) or what falls below the reorder level after the repair (**replenishment order**).
3. Check what the incident has already reserved and ordered before booking anything again.

## Approval
- **Orders above EUR 2,500 need approval by a manager** before they are sent. They get the status "pending approval" and are never sent automatically.
- Urgent orders for a missing repair part are normally small and can go out at once. If a replenishment order is above the limit, it waits for approval and does not block the repair.
- The agent proposes; the system books; a person approves large orders.

## Express ordering
Express delivery costs a surcharge. Use it only when the part is needed for the current repair and normal delivery would arrive after the technician has left. Replenishment orders are never express.

## Contingency stock
Parts that are only needed if something goes wrong (for example a replacement gate valve if an isolation valve fails) are **not reserved**. They are kept as a contingency and named in the summary, with the supplier lead time.

## Equipment
Equipment (excavator, traffic kit, dewatering pump) is reserved like parts. Equipment that is not back at the alarm time shows zero availability. Choose the smallest item that meets the required capacity.

## Honesty
Never state that the package is complete when something is short. The booking reports the real outcome.

## Samenvatting (NL)
Eerst reserveren, dan bestellen. Bestellingen boven EUR 2.500 wachten op goedkeuring van een manager. Express alleen als het onderdeel nodig is voor de lopende reparatie. Onderdelen die alleen nodig zijn als iets misgaat worden niet gereserveerd maar als reserve genoemd.
