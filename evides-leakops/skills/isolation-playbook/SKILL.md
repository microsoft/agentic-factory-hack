---
name: isolation-playbook
description: Step-by-step method for planning the isolation of a burst main from the valves of a pipe segment, covering order, stiff valves, backup valve and critical customers. Use when writing the isolation plan of a diagnosis.
---

# Isolation planning method (SIMULATED skill for the Evides LeakOps demo)

1. **List every isolation valve of the segment** with its distance along the pipe, status and last operation date.
2. **Order them by distance from the break**, the nearest first. A break inside a segment is closed on both sides.
3. **Check each valve:**
   - not open: isolation may not hold, name it and plan a backup;
   - not operated for more than 2 years: treat as stiff, expect 15 extra minutes, plan a backup.
4. **Name the backup valve** (use the nearby-valves lookup) whenever step 3 applies. Say which valve and why.
5. **Check the critical customers** of the section that will lose water. If there are any (care home, hospital), say that the duty manager informs them first.
6. **Write the plan as an ordered list** in the summary: "close A first, then B; B is stiff, backup C; inform the care home first".
7. **Never invent a valve.** Only valves returned by the tools may appear in the plan.
