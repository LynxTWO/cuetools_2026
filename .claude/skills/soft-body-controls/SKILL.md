---
name: soft-body-controls
description: Use when building or restyling controls that model a deformable physical object - rubber keys, dome switches, membrane pads, anything that squashes under a pointer and springs back. Covers the deformation model, pointer-position rendering, spring release, and how to verify motion you cannot see in a still. Sibling to lit-panel-controls, which owns emissive light rather than mechanical response. WPF and Avalonia.
---

# Soft-Body Controls

A control that models an elastic solid: it takes a load, deforms locally, and springs
back. Sibling to `lit-panel-controls`. They share one sentence of philosophy - model the
real object, then let the look fall out of the model - and nothing else:

| | lit-panel-controls | soft-body-controls |
|---|---|---|
| Models | light through plastic | mechanical response of a solid |
| Renders | color and opacity per layer | geometry and displacement |
| Driven by | control STATE (on/off) | POINTER POSITION, continuously |
| Verified by | two states rendered to PNG | position x phase matrix, timed spring samples |
| Fails as | hotspot on the wrong side | a tilting card |

The seam where they meet: a specular highlight that stays put while the surface bends is
the tell that breaks the illusion. **The lit layer must follow the deformation.**

## The shape comes before the motion

The single most important rule, and the one most likely to be learned the hard way: a flat
gradient rectangle tilted rigidly does not read as a soft object rotating. It reads as a
card shearing. This is not fixed by tuning the tilt; it is fixed by giving the object a
shape it can rotate.

Three layers, drawn before anything moves:

1. **Crown** - the bulge catching the light. A radial highlight, hotspot high and centred.
2. **Shoulder** - curvature falloff. A radial that is transparent in the middle and darkens
   toward every edge, so the surface turns away instead of ending flat.
3. **Lip** - the object's own thickness seen edge-on. A bright hairline along the top edge
   and a shadow along the bottom.

Only once the object looks solid at rest does a tilt read as rotation.

**Rubber is matte.** A crown strong enough to look like a highlight on chrome is far too
strong for rubber. If it reads as polished, cut it by half and cut it again.

## The deformation model

Decompose the displacement field into named physical terms, not one blended fudge. For a
cap over a centre plunger (a dome switch), four terms:

    z(sample) = rim(sample) * ( travel + tilt(sample) + dimple(sample) ) * amount

- **Travel** - the plunger collapses, whole-body. Gate it by how much load actually reaches
  the switch: a press far off-axis mostly rocks the cap instead of driving the dome.
- **Tilt** - rigid rotation about the plunger. Positive on the pressed side, NEGATIVE on the
  far side. The far side rising is what makes it a lever instead of a sinking slab.
- **Dimple** - the local well under the finger. **In absolute units, never normalised per
  axis**, or a wide control smears the dimple across its whole width.
- **Rim** - the bond line where the object is fixed to its housing. Falls to exactly zero,
  which is what guarantees nothing escapes the control's own rectangle.

Normalise the term weights so they sum to one against a stated depth budget. Un-normalised
summed gains punch multiples of the budget through the floor at the corners.

**Tune the physics, not the test.** If the far side sinks instead of lifting, the plunger is
taking too much of an off-axis press. That is a parameter with a physical meaning, and
moving it is the honest fix.

## Rendering the depth

If the rim is pinned, the silhouette cannot move, and the depth has to be carried by
shading and perspective instead:

- A **projective** transform (solve a homography through the four displaced corners), not an
  affine one. Affine gives you the card again.
- The **housing recess** behind the object. A receding face reveals whatever is behind it;
  without a recess layer that is the page, and it reads as a rendering gap rather than a
  key sinking into a console.
- The **well's shading** goes BEHIND the label, not over it. Over it, the shading reads as a
  smudge sitting on the text and it costs legibility.
- The well must be clipped to the object's own **corner radius**, or its gradient is cut
  square at the edge while still dark, drawing a hard rectangle across the control.
- **Theme-aware wells.** A near-black object has no range left to darken, so on a dark
  ground the well reads by SUPPRESSING the crown's specular. Adding its own bright rim
  instead draws crescent light artifacts.

## Motion

Collapse is fast (rubber gives way under a finger, roughly 90 ms). Release is a damped
spring that passes its target and comes back.

**Release carries meaning, so make it honest.** A press dragged off the control does not
activate it. Give a landed activation the rubbery overshoot and a cancelled one a dead,
slightly slower release. A control that goes disabled mid-press gets the dead release too,
rather than teleporting from full travel to flat.

Drive the ramp yourself rather than with a framework animation whose completion reverts the
property to its base value; the value here feeds a matrix recomputed per frame.

## Verification

Most of this cannot be seen in a still, and the parts that can are not the parts that
matter. Split it three ways:

1. **The field is pure arithmetic.** Test it with no renderer: corner-deeper-than-centre,
   far-side-lifts, perimeter-is-zero, budget-contained across several aspect ratios,
   monotone in force, centre-press-symmetric, keyboard-press-centred. This is the lane that
   runs in continuous integration.
2. **The input lane is drivable headlessly.** Press point recorded, drag follows, drag-off
   cancels, keyboard activation is centred with no tilt. Assert BEHAVIOUR, not coordinates:
   a pointer position round-trips through window space and is not sub-pixel exact.
3. **Pixels need a real renderer**, and a still cannot show a spring. Capture a position x
   phase matrix, and sample the tail at several timestamps rather than once after it
   settles.

**Guard the projective terms with a test.** A transform transition can silently flatten a
perspective matrix to its affine part with no error and no failure, and the control quietly
becomes a card again. Assert the perspective terms are non-zero.

## Common mistakes

- Tilting a flat rectangle and expecting it to read as three-dimensional.
- Normalising the dimple per axis, so wide controls smear.
- Letting the shading overlay sit on the label.
- A well with its own bright rim, which reads as a lens flare on the surface.
- Forgetting the housing behind a receding face.
- Rewarding a cancelled press with the same satisfying rebound as a real one.
- Assuming the framework's default selector reaches every control that LOOKS like this one;
  a selector matching an exact type misses look-alikes built on a different base class.
