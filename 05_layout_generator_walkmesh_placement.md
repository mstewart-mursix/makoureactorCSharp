Role



Compute safe placements for actors/props using field bounds and walkmesh hints. If the LLM gives positions, validate \& adjust to avoid overlaps/out-of-bounds.



Inputs



Field dimensions, walkmesh (if accessible), collision data.



layout + actors.position hints from plan.



Outputs



Adjusted coordinates; minimal nudging to avoid collisions.



Steps



Bounds Check: Clamp positions into \[0..W],\[0..H].



Overlap Avoidance: Keep simple radius (e.g., 24 px) between actors; nudge along free directions.



Walkmesh Snap (optional): If API exposes walkmesh, project to nearest walkable triangle centroid/edge.



Spawn Point Safety: Ensure spawnPoint not intersecting props.



Acceptance Criteria



No actor overlaps after adjustment.



All placements inside field bounds.



Optional walkmesh snapping guarded by capability check.

