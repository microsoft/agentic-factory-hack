---
name: evidence-citation
description: How to write evidence items in an agent answer so that every claim shows its source and its numbers, including how to cite a knowledge document. Use whenever an answer contains an evidence list.
---

# Writing evidence items (SIMULATED skill for the Evides LeakOps demo)

An evidence item has three parts: **source**, **finding**, **supports**.

1. **source** is the name of the tool that produced the number (for example get-sensor-readings), or, for a knowledge document, its code (the code at the start of its title, for example SOP-ISO-01). Never a file name or a reference id.
2. **finding** is one sentence with the real numbers and units, and the sensor, segment or valve id it is about ("PS-101 fell from 4.89 to 2.85 bar between 00:10 and 03:10").
3. **supports** says which hypothesis the finding favours: leak, no_leak or neutral.
   - A missing innocent explanation (no planned works) supports leak.
   - A harmless explanation (valve work in progress) supports no_leak.
   - Weather, context and every knowledge document are neutral: they inform judgement but prove nothing.
4. **Numbers come from tools, never from documents.** A document can explain what a number means, not replace it.
5. **Three to six items**, the strongest first. At least one is about the alarm sensor itself.
6. **No citation marks** such as 【…】 anywhere in the answer.
