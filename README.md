# Interactive 3D Package Loading Simulator (Unity)

**IIT Bombay IRCC Research Internship Assignment 2**

## Overview
An interactive 3D simulation built in **Unity 6 (C#)** for loading rectangular packages into a delivery truck container while validating spatial boundaries, package overlaps, and maximum weight capacity in real time.

## Key Features
- **Dynamic JSON Loading:** Reads container dimensions (width, height, length, maxWeight) and package specifications (id, dimensions, weight) from input.json.
- **Interactive 3D Controls:**
  - **Left-Click & Drag:** Select and move packages on the horizontal plane.
  - **Right-Click & Drag:** Orbit camera around the container.
  - **R / T / Y Keys (or UI Buttons):** Rotate selected package 90 degrees along X, Y, or Z axes.
  - **E / Q Keys (or UI Buttons):** Adjust vertical height or auto-stack/drop packages.
  - **Z Key (or UI Button):** Undo previous placement actions.
- **Real-Time Validation & Visual Feedback:**
  - **Green Highlight:** Valid placement inside truck boundaries with no collisions.
  - **Red Highlight:** Invalid placement (out of bounds, overlapping another package, or exceeding max weight capacity).
- **JSON Export:** Exports the validated loaded arrangement (id, position, otation, dimensions, weight) to StreamingAssets/output.json.
