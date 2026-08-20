# MoldStress validation log

The full investigation record, moved out of the repository README on 2026-08-18.

It was 571 lines of the README's 883 - a chronological journal in which each new
finding was appended below the last, so the file carried a `RETRACTED:` section
directly above the claim it retracted, two in-plane result tables that disagreed
(1.16x and 0.95x), and rows that had gone stale against the binary (depth peak
position listed as "89%, passes" where `-refcase` reports 53% and FAIL).

None of that is deleted here, because the refuted attempts are the most useful
part: eight configurations of extra terms have been measured and rejected, and
without the record they get proposed again. But a running journal is not a
README, and a reader who wanted to know what the tool does had to read the
history of what it used to do wrong to find out.

**The README now carries current status only, derived by running the cases.
This file carries how that status was arrived at.** Entries below are in the
order they were written and are NOT individually corrected - later entries
supersede earlier ones, and the retraction notices are left where they fell.

---

## Validation, and what currently fails

Against a published injection-moulded TOPAS 6017S-04 plate (100 × 100 × 1.5 mm,
film gate on one edge, polarimetry at 594 nm), on material constants measured by
Kim, Yoon & Kornfield, *Key Eng. Mater.* **326–328** (2006) 183. Run it with
`-refcase`, which exits non-zero unless every clause holds.

> **A GRID BUG INVALIDATED EVERY NUMBER BELOW UNTIL 2026-08-17, AND IT IS FIXED.**
> `FreezeHistory` sampled the cooling curve with `step % 50` into 240 slots, so the
> recorded window was `240 x 50 x dt` — and with `dt = 0.2*dz^2/alpha` and
> `dz ~ 1/n`, **that window shrank as 1/n^2**: ~0.08 s at nz=81 and ~0.005 s at
> nz=321, against a centre that does not freeze until ~3 s. The memory integral
> integrates along that grid, so at fine grids it saw almost none of the cooling
> and the shear channel collapsed. Fixed by single-pass dynamic decimation. The
> comment above that line had described the correct adaptive algorithm; the code
> implemented a different one.
>
> **The "criterion is MET" reading obtained at nz=321 before the fix was an
> artifact of this bug.** On the fixed history the depth ratio is 0.82 and
> `-refcase` exits 2. The failing flow null is what flagged it.

**Convergence, re-taken 2026-08-17 after the fix.** Default configuration:

| nz | in-plane | depth | flow null | verdict |
|---|---|---|---|---|
| 41 | 1.16x | 0.82 | PASS | NOT met |
| 81 | 1.16x | 0.82 | PASS | NOT met |
| 161 | 1.16x | 0.82 | PASS | NOT met |
| 321 | 1.17x | 0.82 | PASS | NOT met |

Flat to three figures across an 8x grid range, where before the fix it drifted
1.17 -> 1.07 -> 0.99 -> 0.95 and 0.89 -> 1.16 -> 1.36 -> 1.43. **nz=41 now gives
the converged answer**, so the expensive `-nz 321` default and the `nz=481`
ceiling are both moot. Shear-only converges too: 0.61x/0.62x/0.62x at nz
41/81/161, against 0.52x/0.26x/0.09x before.

**The in-plane criterion passes at 1.16x; the depth criterion fails at 0.82.**

| Clause | Result | Bar |
|---|---|---|
Measured 2026-08-17 at `-nz 321`, on the paper's own process conditions
(280 °C melt, 150 °C mould, 71.3 MPa) — see the correction note below.

| Clause | Result | Bar |
|---|---|---|
| in-plane peak | **0.95×** the published 1.2 × 10⁻⁴ | within a factor of 2 — **passes** |
| in-plane shape | maximum at the gate, falling only to **97.4%** at the far edge | must decay from the gate — **passes, but see below** |
| gate null | maximum moves from x = 0 to x = 100 mm when the gate moves | must track the gate — **passes** |
| depth ratio | **0.99** surface/deep against a published **2.78** | [1.39, 5.56] — **fails** |
| depth peak position | maximum at **89%** of the half-wall | beyond 75% — **passes** |
| depth null | **1.35 vs 0.81** after being rebuilt — see below | must invert — **passes** |

**Two rows moved on 2026-08-17, in opposite directions, and neither move was
the point of the change.** Depth peak position went 68% → 89% and now passes.
In-plane shape went 70.6% → 97.4% at the far edge, i.e. the predicted field is
now nearly FLAT across the plate where the reference falls roughly linearly to
zero. It still passes, and that is a criticism of the clause rather than a
defence of the model: "maximum on the gate side" is satisfied by a 2.6% tilt.
A shape clause that a nearly-flat field passes is not measuring shape.

**The depth null now works — it was the null that was broken, not the channel.**
Corrected 2026-08-17, third attempt. Earlier that day this README claimed the
depth channel was "not driven by the freeze history at all", on the evidence of
a null that would not move and a 1% response to a 30 °C mould change. **That
claim was wrong, and a positive control refuted it:** scaling every freeze time
by 100 moves the ratio 0.810 → 0.812, but scaling by 0.01 moves it to 0.908.
The channel responds to freeze times being SHORTENED and is insensitive to their
being LENGTHENED — and that insensitivity is correct physics, because once a
layer has vitrified reduced time stops accumulating and a later nominal freeze
time adds nothing to the integral.

The old null inverted `t → tMax − t`, which **lengthens** the freeze time at both
sampling depths (0.002 → 6.9 s at the wall, 0.85 → 6.05 s at 47%). It perturbed
exclusively in the direction the model is provably deaf to. It was a
rearrangement pointing the wrong way — the third version of this null that could
not fail, after an `Array.Reverse` on a mid-plane-symmetric profile.

The null now mirrors the depth axis of the **temperature history** as well as the
freeze times (|z| → h/2 − |z|), giving the wall the core's thermal history. That
inverts the driver the memory integral actually reads, rather than a derived
label. It discriminates: **1.35 vs 0.81.**

A hypothesis checked and killed on the way: the memory integral's clamp was the
obvious suspect, since a clamped quantity is deaf to its inputs. It is
instrumented now and **does not bind — 0 of 39,700 evaluations saturated,
largest raw value 0.2.**

**Where the depth deficit actually is.** The skin signal comes entirely from
fountain deposition, and it saturates:

| fountain strain | depth ratio |
|---|---|
| 0 (shear only) | **0.02** |
| 1 (shipped default) | 0.81 |
| 3 | 1.03 |

The shear channel contributes essentially **nothing** at the wall. That is
self-consistent rather than a bug: shear stress is largest at the wall, but a
layer that vitrifies on contact never deforms, so σ relaxes toward τ and never
gets there. The physical answer is the one Mavridis, Hrymak & Vlachopoulos
(*J. Rheol.* **32**(6) 639, 1988) give — skin material was not at the wall when
it was deformed. It was oriented in the hot core and carried to the wall by
fountain flow, then quenched. The model has that mechanism but treats it as a
locally computed strain that then relaxes, which is why tripling it buys only
27%.

**That change was implemented and it is measurably worse.** Available as
`-frontmode carried`, not the default. Measured at nz=81:

| | in-plane peak | depth ratio | depth null |
|---|---|---|---|
| extensional (default) | **1.07×** passes | 0.81 fails | passes |
| melt orientation carried | **4.57×** fails | 1.09 fails | **fails** |

The diagnosis of the *cap* stands: the extensional form cannot exceed the
plateau modulus (`eEff → 1` as Wi → ∞, so σ ≤ G = 2.8 × 10⁵ Pa), while the
melt's own wall shear stress here is ~5 × 10⁵ Pa, so the cap was binding on the
wrong quantity. **The implementation is what failed.** `2·τ_wall = dp/ds·(h/2)`
has no z-dependence, so it lifts every depth by the same amount and leaves
`exp(−ξ)` as the only thing separating skin from core. That inflated the
thickness average fourfold, moved the ratio by 0.28, and flattened the profile
enough to kill the null again.

What it exposes is the real missing piece: **not every depth is front-deposited.**
Material near the mid-plane is the core stream and is never swept to the wall,
so the deposition term needs a weight that falls off inward.

### The deposition weight, and what it revealed

That weight is in the literature and is parameter-free. **Blake's
maximum-residence envelope**, in M. C. Altan, *A Review of Fiber-Reinforced
Injection Molding: Flow Kinematics and Particle Orientation*, J. Thermoplastic
Composite Materials **3** (Oct 1990) 275, §2.4.4; pathlines measured by Coyle,
Blake & Macosko, *AIChE J.* **33**(7) 1168 (1987). Sorting particles by whether
they ever reached the front gives a dividing height of `1/√3` and an envelope
`x1m = (3/2)(1 − x3m²)`, i.e. a support boundary

    z*(s) = sqrt(1 − (2/3)·s/L)      in units of the half gap

Material inside `z*` never passed through the front and receives no deposition.
Implemented as `-deposition-support`. **It is not the default, and the reason is
the interesting part.** Measured at nz=81:

| | in-plane peak | depth @ gate (criterion) | depth @ s/L 0.1–0.5 |
|---|---|---|---|
| without envelope | **1.07×** passes | 0.81 fails | 0.81 |
| with envelope | **0.28×** fails | 0.02 fails | **2.52** vs published 2.78 |

**At every interior station the depth ratio is 2.52 against a published 2.78** —
9% low, inside the band, with no fitted parameter. The depth *shape* problem is
essentially solved by a kinematic result taken off the shelf.

Two things break, and both are diagnostic rather than incidental:

1. **The in-plane peak collapses, 1.07× → 0.28×**, because the core value falls
   1.39 × 10⁻⁴ → 4.48 × 10⁻⁵ once the core stops receiving deposition. That is
   the finding: **the in-plane agreement at 1.07× was propped up by depositing
   fountain orientation into the core, where the literature says none is
   deposited.** Removing it exposes that the shear channel under-predicts the
   core by about 3×. That is a better-located problem than "the skin is 10× low",
   and it is in the channel that has always been the model's weakest.
2. **The registered criterion samples at s = 0, and `z*(0) = 1`** — the gate is
   the one station where the envelope admits no deposited material at all. The
   boundary crosses the criterion's own surface sampling point (0.975 of the
   half-wall) at s/L = 0.075, so only the first ~7% of the flow length is
   affected. The reference paper measured its depth profiles at positions A, B
   and C and publishes coordinates for none of them, so **the criterion's station
   has NOT been moved to suit** — the ratio is reported across stations beside it
   instead. This is the fifth time a number on this model has turned on a
   sampling definition before it turned on physics.

### The in-plane peak is now the maximum of the profile

Corrected 2026-08-17. The clause reads *"predicted peak within a factor of 2 of
1.2 × 10⁻⁴"*, and the code took `avg[0]` — the same thing only if the maximum
sits at the gate. That held for every model this case had run until Blake's
envelope arrived, and `z*(0) = 1` admits no deposited material at the gate edge
exactly, so `avg[0]` collapsed to the shear-only value and the clause fell
1.07× → 0.28×. **That was the criterion reading the one station where the
kinematics is singular, not the model losing its peak.**

Taking the actual maximum is the literal reading and does not weaken anything:
clause (b) still separately requires that maximum to lie on the gate side and to
decay toward the far edge. The gate-edge value is printed beside it. The change
is **inert in the default configuration** — the maximum is at s = 0 there, so
1.07× is unchanged.

Under the envelope the peak recovers to **0.59×, inside the factor of 2**. But it
now fails a different clause, and this one is physical rather than a sampling
artefact:

| envelope | in-plane peak | peak location | far-edge value |
|---|---|---|---|
| off | 1.07× passes | s = 0 mm | 76.1% — decays, **passes** |
| on | 0.59× passes | **s = 92 mm** | **129.3% — rises, FAILS** |

**Blake's envelope makes the fountain-deposited layer thicken along the flow, so
predicted birefringence RISES with distance from the gate. The reference says it
falls roughly linearly to zero.** This is exactly the gate-versus-far-field
tension flagged before the envelope was implemented, when it looked like it might
be confined to the first 7% of flow length. It is not: it inverts the whole
along-flow profile.

So the envelope trades a depth-shape success for an along-flow-shape failure, and
it stays opt-in. What it has genuinely established is that the depth ratio and
the along-flow decay are coupled through one term, and no single scaling of that
term satisfies both.

### An along-flow decay for the deposition — implemented, measured, not adopted

`-deposition-decay` scales the front deposition by the shear window available to
the melt **feeding** the front at that station (the memory bracket at the
mid-plane, the core stream the front draws from). The argument is not a new one:
it is the same expression that already gives the shear channel its gate-to-edge
decay — at the far edge the melt arrives as filling ends, the window is
identically zero, and there is no orientation to deposit.

It does produce decay, and it is still wrong. Measured at nz=81 with the
envelope on:

| | in-plane peak | peak location | far edge | depth @ s/L 0.1–0.5 |
|---|---|---|---|---|
| envelope only | 0.59× | s = 92 mm | 129% — rises | **4.08** |
| envelope + decay | **0.31×** | s = 87 mm | 0% — falls | 1.56 |

The far-edge rise is fixed, but the profile is now a **hump**: it climbs from the
gate to a maximum at 87% of the flow length and then collapses to zero. The
reference falls roughly linearly *from the gate*. And both magnitudes get worse —
in-plane 0.59× → 0.31×, depth 4.08 → 1.56.

The mechanism is visible in the two terms. Blake's support **grows** with
distance (z* falls from 1 to 0.577) while this window factor **falls** slowly and
then crashes at the very end. Their product peaks near s/L = 0.87. No scaling of
either fixes that — a monotone decay from the gate needs the magnitude to fall
faster than the support grows, everywhere, and this factor does not.

**It also exposed a weak clause.** *"Maximum on the gate side"* is implemented as
an endpoint comparison, `profile[0] > profile[last]`, so a profile peaking at 87%
of the length **passes** it. It passed here while the shape was plainly wrong.
The peak location is now printed beside the verdict so the gap is visible; the
registered clause itself is left alone rather than quietly tightened.

### No magnitude term can rescue the envelope — this is a proof, not a measurement

The deposited layer's thickness fraction is `f(s) = 1 − z*(s) = 1 − √(1 − ⅔·s/L)`,
and **`f(0) = 0` exactly**. The thickness-averaged deposition at the gate is
therefore zero for *any* magnitude term `M(s)`, because `M` multiplies a layer of
zero thickness.

That forecloses the whole search. The in-plane clause needs the maximum on the
gate side; with deposition contributing nothing there, the gate value is pinned
at the shear-only value, **0.26× of published**. Any `M` large enough to reach the
factor-of-2 bar (0.5×) necessarily puts the maximum somewhere `f > 0` — away from
the gate — failing the shape clause. **The two clauses are mutually exclusive
under a hard envelope support, independently of `M`.**

Confirmed numerically, with a control separating the deposition term from overall
grid drift:

| grid | shear-only at gate | envelope at gate | deposition at gate |
|---|---|---|---|
| nz=41 | 6.233e-5 | 6.781e-5 | **5.48e-6** |
| nz=81 | 3.071e-5 | 3.348e-5 | **2.77e-6** |
| nz=161 | 1.037e-5 | 1.176e-5 | **1.39e-6** |

The deposition at the gate halves with every grid doubling — converging to zero as
the analysis requires. Its nonzero value on coarse grids is a **one-node
discretisation artifact**: `z*(0) = 1` makes the support a measure-zero set that
the grid nonetheless resolves with a single node.

**What must change is the support, not the magnitude.** And there is a reason to
doubt `z*(0) = 1` physically: Blake's envelope classifies material by whether it
was *transported* to the wall from upstream. It says nothing about the material
that **constituted the initial front** at the gate, which was itself
fountain-processed and laid onto the wall there. `z*(0) = 1` is a statement about
transport history, not evidence that gate-wall material never saw a front.

**A separate defect the control exposed, and it is not about the envelope:** the
shear-only gate value is itself strongly grid-dependent — 6.233e-5 → 3.071e-5 →
1.037e-5, still falling steeply at nz=161. **The in-plane number is not converged
with the fountain off.** The convergence sweep behind the shipped `-nz 321`
default was run with the fountain ON, so it never covered this configuration.
Any future work on the shear channel at the gate needs its own sweep first.

### RETRACTED: the null failure under `-thinned-lambda` was NOT an instrument problem

**The section below is wrong and is kept only so the error is visible.** It
concluded, from the probes, that the null was "aimed where the model cannot
respond". Measuring the memory profile itself refutes that:

| depth | mem_default | mem_thinned |
|---|---|---|
| 95% | 0.0024 | **0.9708** |
| 85% | 0.0442 | **1.0000** |
| 75% | 0.1382 | **1.0000** |
| 50% | 0.4509 | **0.9973** |
| 25% | 0.6699 | **0.9591** |
| 5% | 0.7092 | **0.9193** |

Under the thinned lambda the retained fraction is **0.92 to 1.00 across the whole
depth**, against 0.002 to 0.71 for the default — a factor of 300 collapsed to
nothing. **The memory bracket is saturated everywhere**, so the model has no
freeze-ORDER dependence left to detect, and the null correctly reports FAIL.

**Why the probe evidence misled me.** The x0.01 freeze-time scaling moves the
answer 79-92%, which I read as "the channel is responsive, so the null's
direction must be the problem". It moves the answer because it drags layers OUT
of saturation — a magnitude effect. It says nothing about whether ordering
matters, and ordering is what the clause names. **A probe that de-saturates is
not evidence that the saturated quantity has structure.**

**What this means for `-thinned-lambda`, and it is worse than a failing control.**
Memory near 1 everywhere means every layer retains essentially all of its shear
orientation — the model has stopped representing relaxation at all, including for
a core that stays molten for 4 s after filling ends. Its depth ratio of 1.91 is
obtained by switching off the physics the depth profile is supposed to come from.
That is a right answer for the wrong reason, and it is exactly what the null
exists to catch. **The null needed no rebuilding; the fifth version would have
been built to silence a correct alarm.**

### The null failure under `-thinned-lambda` is an instrument problem, not evidence

Checked 2026-08-17, because "the control fails" would otherwise read as a verdict
on the physics. The probes are the positive control on the null and they separate
the two possible causes:

| config | null (mirror T history) | probe x0.01 | probe x100 | max memory |
|---|---|---|---|---|
| default | 2.384 vs 0.583 — **+309%** PASS | 1.303 vs 0.820 (+59%) | 0.820 (0%) | 0.7 |
| `-thinned-lambda` | 1.602 vs 1.757 — **-9%** FAIL | 0.406 vs 1.92 (**-79%**) | 2.024 (+5%) | **1.0** |
| `+ -complementary` | 1.896 vs 1.714 — **+11%** FAIL | 0.147 vs 1.91 (**-92%**) | 2.036 (+7%) | 1.0 |

Under the thinned lambda the channel moves **79-92%** for a freeze-time
perturbation while the null's own perturbation moves it **9%**. The subject is
not deaf; the null is aimed where the model cannot respond. Same class as the
`t -> tMax - t` version, reached by a different route.

**The mechanism, and it is visible in the last column.** The retained fraction
reaches **1.0** under the thinned lambda against 0.7 under the default — the
physical ceiling of the memory bracket. Mirroring the temperature history makes
some layers hotter and some colder, and the ones it would push *up* are already
at full retention, so a large part of the profile cannot move in the direction
the perturbation pushes. The asymmetry is the same one the default shows
(responds to shortening, deaf to lengthening); the thinned lambda simply moves
more of the depth range into the saturated part of it.

**The clamp is again NOT the cause** — 0 of 95,600 evaluations saturated in every
configuration. That hypothesis has now been tested and refuted twice by counting
rather than assumed either way.

**What follows for the thinned-lambda result.** Its failing null is not a reason
to reject it — but its passing depth ratio is not validated either, because the
control that would guard it cannot discriminate in that regime. A fifth version
of this null needs a perturbation that stays in the responsive direction; the
x0.01 freeze-time scaling demonstrably does, though it tests sensitivity to the
freeze-history *magnitude* rather than to its *ordering*, which is what the
registered clause names.

### The thermal channel is not the deficit — checked before changing it

The Lagrangian model leaves the core relaxed to near-zero flow orientation, so
the published core plateau of 1.8 × 10⁻⁴ has to come from somewhere. The thermal
channel supplies about 7 × 10⁻⁶ there, which looks like a 26× deficit and an
obvious thing to go and fix.

**It is not a deficit.** The channel is driven by the freeze-off gradient — each
layer's temperature at the moment the centre solidifies, with the mean and linear
parts removed by force and moment balance. That gradient spans `Tg − T_mould`,
and this part is moulded at **150 °C against a Tg of 178 °C**, i.e. **28 K**.
Scaling the standard free-quench magnitude `Eα ΔT / 3(1−ν)`:

| mould | ΔT | σ | dn_thermal | vs published core |
|---|---|---|---|---|
| **150 °C** (this part) | **28 K** | 2.6 MPa | **2.2e-5** | 12% |
| 120 °C | 58 K | 5.4 MPa | 4.6e-5 | 26% |
| 80 °C | 98 K | 9.2 MPa | 7.8e-5 | 43% |
| 20 °C | 158 K | 14.8 MPa | 1.26e-4 | 70% |

The model's ~2–4 × 10⁻⁵ is the right order **for this mould**. The 1.26 × 10⁻⁴
figure that makes the deficit look damning assumes a cold mould and cooling to
ambient — a different process. A 150 °C mould is a deliberately hot one, chosen
precisely because it produces low thermal residual stress in an optical part, and
the model reproduces that.

**So the target was wrong.** If the thermal channel is right and the core plateau
is 1.8 × 10⁻⁴, the core birefringence is **residual flow orientation that both
models relax away** — the Eulerian one by construction, the Lagrangian one by
relaxing σ toward a decaying packing stress over the 3 s the core stays molten.
That is where the remaining deficit lives, and it is the same quantity the
shear-thinned λ reached by saturating it.

No code changed. The 26× figure is withdrawn as a comparison against the wrong
process rather than a defect in the channel.

### Why the profile peaks at 50%, and why no further term will fix it

Measured cause, on the fixed freeze history at the corrected conditions:
`mem_wlf` **rises inward** — 0.000 at the wall, 0.138 at 75%, 0.451 at 50%, 0.710
at the mid-plane — while `tau_visc` falls linearly from the wall. Their product
peaks mid-depth. The reference peaks at the skin.

**That is not a missing term. It is the shear channel's Lagrangian assumption.**
It computes, for each depth, the stress a fluid element would build up *having sat
at that depth since t = 0*, sheared at the local rate until it freezes. Under that
assumption the wall layer must retain nothing — it freezes at 0.094 s, before it
can build anything — and the core must retain the most, because it stays molten
longest. The profile it produces is the correct answer to the wrong history.

In fountain flow the skin never sat at the wall. It was deformed in the hot core,
carried to the wall by the advancing front, and quenched on arrival — so it
retains what it was already carrying. The two channels disagree about the same
material, which is why removing the double-count changed the magnitude without
changing the shape.

**Eight configurations measured against this, none adopted** (nz=161; in-plane bar
is a factor of 2, depth band [1.39, 5.56], depth read at the criterion's station):

| configuration | in-plane | peak at | far edge | depth |
|---|---|---|---|---|
| default | **1.16×** | 0 mm | 46.5% | 0.82 |
| carried melt orientation | 3.48× | 0 mm | 82.1% | 1.47 |
| + Blake envelope | 1.93× | 92 mm | **268%** | 0.31 |
| + complementary gate | 1.87× | 99 mm | **268%** | 0.31 |
| + thinned λ | 2.46× | 15 mm | 76.8% | 1.91 |
| thinned λ alone | 2.87× | 0 mm | 18.9% | 1.92 |
| thinned λ + complementary | 2.35× | 4 mm | 14.8% | 1.91 |
| Blake envelope alone | 0.70× | 51 mm | 54.8% | 0.31 |

Two structural results close off whole families rather than single attempts.
**Every envelope configuration reads 0.31 at the criterion's station**, because
`z*(0) = 1` admits no deposited material at the gate — the `f(0) = 0` argument,
which is analytic. And **the only lever that moves the depth ratio is the
shear-thinned λ**, which works by saturating the memory bracket to 0.92–1.00
everywhere: it does not correct the depth dependence, it removes it.

**What would actually fix it is not a term but a history.** The shear channel
needs each element's own path — where it was, how hard it was sheared, when it
arrived — rather than a standing assumption that it never moved. That is a
Lagrangian particle model, and it is a different program from the one here.

### The depth criterion now uses both channels

Corrected 2026-08-17. The depth clause compared `DnFlow` alone against a profile
the source measured in the **xz and yz planes** — out of plane, on slabs cut from
the plate and viewed edge-on — where the thermal residual stress contributes in
full. Isayev (*J. Polym. Sci. B*, 2006) has the thermal part dominating the core
outright. Comparing one channel against a two-channel measurement is a
measurement-definition error of the same class as the withdrawn 5.56.

**The in-plane clause is deliberately unchanged.** Thermal stress is equibiaxial
in plane (σxx = σyy, σzz = 0), so it contributes exactly **zero** to the in-plane
difference that clause measures. Adding it there would be adding a term that
vanishes in the geometry it is measured in. In-plane stays 1.07×.

| | depth @ gate | depth @ s/L 0.1–0.5 | published |
|---|---|---|---|
| flow only, no envelope | 0.81 | 0.81 | 2.78 (yz) / 4.67 (xz) |
| **flow+thermal**, no envelope | **1.16** | 1.16 | |
| flow only + envelope | 0.02 | 2.52 | |
| **flow+thermal + envelope** | 1.08 | **4.08** | |

With both corrections applied, the interior-station ratio is **4.08 — between the
two planes the source actually measured** (2.78 yz, 4.67 xz), and 13% below the
cross-plane value. That is the closest this model has come, on a corrected
reference and a literature deposition weight, with no fitted parameter.

**It cost the null, and the null has been rebuilt — fourth version.** Correcting
the clause dropped the freeze-order null to 1.19 vs 1.16, just under the bar.
Nothing about the perturbation got worse: the thermal channel is nearly flat
through the thickness, so adding it to numerator and denominator alike drags any
ratio toward 1 and compresses whatever the null was resolving. **A single null on
a summed quantity is always diluted by whichever channel is flatter, and a bigger
kick does not fix that.** So the control is decomposed the same way the
measurement is:

| control | what it perturbs | requirement | result |
|---|---|---|---|
| **(i) flow** | mirror the temperature history | flow-only ratio must move >50% | 1.349 vs 0.812 — **passes** (2.090 vs 0.018 with envelope) |
| **(ii) thermal** | CTE = 0 | total must collapse **exactly** onto flow-only, **and** differ materially when on | rel err 0.0, contribution 43% — **passes** |

Clause (ii) has two halves that fail in opposite directions, deliberately. The
collapse identity alone would hold **trivially if the thermal term were never
added at all**, so a check built only on it would pass when the feature is
absent. The second half requires the channel to move the reported ratio.

And the identity check carries its own positive control: the same comparison is
fed a pair it must reject (thermal-ON total against flow-only) and required to
report a difference. It reads 3.5 × 10⁻¹, so the tolerance is not swallowing
everything.

The `s/L = 1.0` column reads infinity: at the far edge the deep sample sits
inside `z*` and the shear channel is identically zero there, so the denominator
is exactly 0. Reported rather than suppressed — it is a real degeneracy of
sampling a ratio at a station where the denominator vanishes.

**What the failure means.** The predicted profile is now core-weighted where the
real part is skin-peaked. That is not a missing magnitude — it is the balance
between the two channels through the thickness. Shear correctly gives a
fast-freezing skin almost nothing, and fountain deposition supplies what the skin
has, so the depth shape is set by how those two trade off, which is the open
question. The in-plane peak, by contrast, is 0.95× of the published value on
measured constants with **no fitted parameter between the two channels.**

**Numbers here are the default: `-refcase` now runs at nz=321.**  They are still drifting.

> **The sweep below was taken at the OLD process conditions (290 °C / 120 °C /
> 60 MPa) and its VALUES are superseded** by the 2026-08-17 correction — at
> 150 °C the pair is 0.95× / 0.99 at nz=321 and 0.81 depth at nz=81. Convergence
> expires when the model changes. The GRID at which it converged plausibly
> carries over, and that is all the sweep is still quoted for.

Neither
registered number is converged at the shipped default of 81: peak 1.01× / depth
0.74 at nz=81, 0.90× / 0.91 at 161, 0.85× / 0.98 at 321. The trend is monotone
and decelerating, and extrapolates to roughly 0.83× and 1.0. **nz=641 does not
complete** — the freeze solver is explicit, so cost grows as the cube of the node
count and the step cap is reached before the core freezes. So convergence here is
demonstrated by trend, not by brute force, and `-refcase` warns if you drop below it. `-run` now runs the physics at nz=321 too and exports a small **wall-clustered** subset of those same nodes, so a converged model no longer forces an unmanageable file. Depths are placed quadratically in distance from the wall — dense at the skin, where all the structure is, sparse in the core — and taken as actual physics nodes, so nothing is interpolated. `-nzexport` is an upper bound rather than a count: near the wall several requested depths collapse onto the same node and are de-duplicated, so 41 requested yields 19 distinct.

**The deficit is located and it is not a shape problem.** At the surface the
model gives ~1.0 × 10⁻⁴ against a published 10 × 10⁻⁴ — a factor of ten low —
while at the published 0.4 mm depth it gives ~1.1 × 10⁻⁴ against 1.8 × 10⁻⁴,
which is within a factor of two. The core is roughly right and the skin is short
by an order of magnitude. Sampling depth is not the explanation: sweeping the
surface sampling point from 19 µm to 0.1 µm moves the value by under 1%.

**Three candidate explanations for the depth deficit have been eliminated by
measurement rather than argument:** the melt stress-optical coefficient (the
depth ratio is invariant under any scaling of it), the fountain term's magnitude
(the physically correct version is *smaller* than the arbitrary one it replaced,
and the profile needs more skin weighting, not less), and grid resolution. What
remains is the shear channel's behaviour in the skin.

Options: `-writecatalog [-out <agf>]`, `-gates`, `-run`, `-refcase`, `-selftest`,
plus `-file <zmx>` (headless batch mode), `-gateconfig <file>`, `-outdir <dir>`,
`-filltime`, `-packpressure`, `-packtime`, `-melttemp`, `-moldtemp`,
`-materials A,B`, `-directindex`.

---

# Second consolidation, 2026-08-18 (evening)

The README's validation section had grown from 441 to 686 lines across ten
commits in one day, and had accreted two structural faults worth naming because
both are the same shape as faults this project found elsewhere the same day:

- **The cases ran 1, 3, 2.** Case 3 was inserted by anchoring on case 2's
  heading, which is exactly the misordering found in `verify-the-artifact` that
  morning (its questions ran Q1-Q5, Q7, Q6, from a patch appended in the wrong
  place). Same defect, same cause, six hours apart.
- **A heading that no longer described its contents** - "The two open failures"
  above a section covering three cases and five open items.

And one stale row, found the same way as the four found in the first
consolidation: by re-deriving from the binary rather than reading the prose. Case
2's layer-removal points read 32.4 / 44.7 / 48.9 / 50.0 where the README said
32.6 / 45.0 / 49.1 / 50.0 - they had moved when the thermal channel changed and
nobody re-took them.

The full text cut in that consolidation follows.

---

## Validation, and what currently fails

Two published reference cases, run with `-refcase` and `-refcase2`. Both exit
non-zero unless every clause holds, and **both currently fail** - the tool is
usable as an estimate and is not validated as a predictor.

Numbers below are read from the binary at `-nz 161`, not carried in prose.
How they were arrived at, including every mechanism tried and rejected, is in
[`extensions/MoldStress/VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md).
Candidate sources for further checks are in
[`extensions/MoldStress/VALIDATION-SOURCES.md`](extensions/MoldStress/VALIDATION-SOURCES.md).

### Case 1 - TOPAS 6017S-04 plate

100 x 100 x 1.5 mm, film gate on one edge, polarimetry at 594 nm, 280 C melt /
150 C mould / 71.3 MPa. Material constants from Kim, Yoon & Kornfield,
*Key Eng. Mater.* **326-328** (2006) 183.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 1.398e-4 against a published 1.2e-4 - **1.16x** | within a factor of 2 | PASS |
| in-plane shape | maximum at the gate, 46.5% of it at the far edge | must decay from the gate | PASS |
| gate null | peak moves x=0 -> x=100 mm when the gate moves | must track the gate | PASS |
| depth ratio | **3.44** surface/deep against a published 2.78 | [1.39, 5.56] | PASS |
| depth peak position | maximum at **93%** of the half-wall | beyond 75% | PASS |
| depth null (flow) | denominator collapses to exactly 0 with the freeze order mirrored | must respond | PASS |
| depth null (thermal) | CTE=0 collapses to flow-only, and the channel is material | must collapse | PASS |

**The thermal channel accumulates INCREMENTALLY on all three cases since
2026-08-18** (`-snapshot` restores the old single-instant construction). Case 2
is insensitive; case 1's depth ratio moves 3.43 -> 4.12.

**The thermal over-contribution that flip exposed is now fixed, and the fix is a
boundary condition rather than a constant.** Completing the cooling after every
layer is solid put the thermal channel at 26% of flow at the surface sampling
depth, against a published 8% for this material class. A freely quenched sheet
MUST have that increment - without it case 3's core reads 7.6e-7 against a
published 7e-4 - but a moulding must not: at that stage the part is fully solid
and still adhered to the cavity, so it cannot relieve in-plane, and on ejection
the denied contraction is identical for every layer and cancels.

That last clause was implemented and MEASURED rather than assumed. A
constrained-then-released branch returned identically ZERO, which refuted it as a
general construction and simultaneously justified excluding the increment for a
moulding. Case 1's thermal share is now 14% at the surface and 6% at the deep
point, bracketing the published 8%, and the depth ratio reads 3.44.

**Cooling after EJECTION contributes exactly zero, and that is a proof rather than
an omission** - retracting an earlier claim here that called it "the larger half
for a cold mould". After ejection the part is entirely glassy: no layer
vitrifies, there is no sub-Tg relaxation mechanism in this model, and every layer
carries one modulus. A linear-elastic body taken through a thermal cycle that
starts uniform and ends uniform has zero residual stress - the transient stresses
are real but fully recovered. The condition is mould < Tg, and this tool already
ENFORCES it (`FreezeHistory` refuses anything else outright, "need mould < Tg <
melt"), so the result is unconditional here rather than a property of the cases
that happen to be run.

A warning for the mould-above-Tg case was written and then removed as dead code.
With case 1 set to a 200 C mould against a 178 C Tg it never printed, because the
run fails earlier at the freeze solve. What guards the proof is the precondition,
not a warning - so the self-test now asserts that instead, in both directions: a
mould at or above Tg is REFUSED, and one below is accepted.

**`-refcase` now reports the registered criterion as MET.** It did not before
2026-08-18; the depth ratio was 0.82 and the peak sat at 53% of the half-wall.
What changed is the depth history, not a constant - see below. `-eulerian-depth`
restores the previous behaviour exactly, including both failures.

**What that pass does and does not establish, added 2026-08-18 after a literature
sweep.** The depth-peak clause samples the surface at 97.5% of the half-wall,
which on this 1.5 mm plate is **18.8 um from the wall**, and the ported shape
peaks at 93%, i.e. **52.5 um in**. Flaman (TU Eindhoven, 1990) reports that
birefringence could not be resolved within ~60 um of the surface, and that the
maximum the literature calls the "skin peak" therefore sits at z/H ~ 0.75-0.8 -
about 165 um in. **Both this criterion's sampling point and the new model's peak
lie inside a band at least one careful study says its instrument cannot resolve.**
The pass stands (different paper, material and instrument, and this case's own
0.2 mm slab question was already open) but it means "the model now peaks in the
outer quarter", not "the model gets the depth profile right" - the data that
would separate 78% from 93% is not in any source found. See
[`VALIDATION-SOURCES.md`](extensions/MoldStress/VALIDATION-SOURCES.md), section 1
of the third sweep.

Converged at **nz=41**, which is the default since 2026-08-18. Re-taken on the
shipped configuration rather than carried over from the Eulerian sweep: depth
ratio 3.43 / 3.46 / 3.45 / 3.43 and in-plane peak 1.16x / 1.16x / 1.16x / 1.17x
at nz 41 / 81 / 161 / 321, so nz=41 lands on nz=321's depth ratio to three
figures. nz=21 was measured and is NOT converged - ratio 3.34 with the peak
pinned at the wall - so 41 is a floor with something below it rather than the
smallest grid that passed. The case runs in 15 s where nz=321 took 6m29.

### Case 3 - free quench, and the first test of the THERMAL channel alone

Bisphenol-A polycarbonate, 2 mm sheet, quenched 160 C -> 60 C. Wimberger-Friedl,
PhD thesis, TU Eindhoven (1991) ch. 3.2, open access. Run with `-refquench`.

Cases 1 and 2 are mouldings, so every number in them is flow and thermal
together; the thermal channel had only ever been tested by NULLING it. A quench
has no flow, and `ThermalProfile` reads only the freeze history - so this case
needs no gate, flow rate or fill time, and none of case 2's unsourced inputs can
reach it.

| Clause | Result | Bar | |
|---|---|---|---|
| sign reversal | core +5.4e-4, surface -9.5e-4 | must reverse | PASS |
| direction | core tension, surface compression | as published | PASS |
| zero crossing | **z/d 0.572** (published 0.5-0.8) | [0.40, 0.90] | PASS |
| shape ratio | \|surface\|/\|core\| **1.76** (published 1.7-4.0) | [1.0, 8.0] | PASS |
| magnitude | \|surface\| 9.5e-4 against a published 1.75e-3 | within 3x | PASS |
| null | CTE=0 collapses the profile to exactly 0 | must collapse | PASS |
| control on the null | CTE restored gives 9.8e-4 | must not be dead | PASS |

**The thermal channel now accumulates stress INCREMENTALLY** - each layer becomes
elastic when it vitrifies, and every later cooling increment re-equilibrates over
the layers solid at that moment, so the total is force- and moment-balanced
without imposing it. The previous construction evaluated the temperature profile
at one instant (when the centre hits Tg) and removed its mean and linear parts.
`-snapshot` still runs the old one.

| | snapshot | incremental | published |
|---|---|---|---|
| core | 5.4e-4 | 5.4e-4 | +5..+9e-4 |
| surface | -9.5e-4 | **-1.42e-3** | -1.5..-2.0e-3 |
| ratio | 1.76 (bottom edge) | **2.64** (centre) | 1.7-4.0 |
| crossing | 0.572 | 0.649 | 0.5-0.8 |

**A defect found on the way, and it was in a shared component.** FreezeHistory's
cooling loop runs `while (snapshot == null)` and stops the moment the centre
crosses Tg - all the flow channel ever needed. But the centre vitrifies ~85 C
above the bath with the skin already cold, and that differential contraction is
what puts the core in tension. Integrating only the recorded window left the core
at 7.6e-7 against a published 7e-4 and the ratio at 627. Completing it needs no
change to the shared solve: once every layer is solid the solid set stops
changing and the accumulated stress depends only on the TOTAL remaining dT, not
the path, so the rest of the cooling is exactly one increment.

**The Ti trend is still NOT reproduced, and failing to fix it is the sharper
result.** The source reports the zero crossing moving OUTWARD as
the initial temperature rises, z/d ~0.3 to ~0.85, naming Ti the dominant control.
The model moves it INWARD, 0.589 -> 0.565 across Ti 150-180 C - wrong direction
and a span 23x too small. Printed as an unscored TREND DIAGNOSTIC rather than
folded into the verdict, because registering a clause after seeing its result is
moving the bar.

The snapshot construction could not move the crossing at all. The incremental one
CAN: with post-vitrification cooling excluded it spans **0.375 -> 0.874**, against
a published 0.3 -> 0.85 - almost exactly right. But including that cooling, which
the values above demand, flattens it to 0.645 -> 0.656, because every layer then
cools from about Tg to the bath REGARDLESS of Ti.

So the elastic stress is dominated by a Ti-independent term, and the
Ti-dependence must live in the mechanism the source names and this channel lacks:
**frozen-in ORIENTATION from stresses above Tg**, where time-above-Tg is exactly
what Ti controls. That is a second thermal channel, not a correction to this one -
and it is the same orientational mechanism the source says dominates the quench
birefringence in the first place. **The channel now gets the structure and the
magnitudes right and is missing the orientational half, which is what was
predicted in writing before the case was ever run.**

Converged in shape, drifting at the surface node: crossing 0.581 / 0.575 / 0.572
/ 0.571 and ratio 1.50 / 1.67 / 1.76 / 1.81 at nz 41 / 81 / 161 / 321. Default is
161; at nz=41 the ratio falls below the published range.

### Case 2 - ZEONEX 480R plano-convex lens

32 mm diameter, 2 mm centre thickness, 0.8 mm edge gate, 275 C / 124 C,
98.10 MPa. Chang, Yu, Chiu, Yang, Lai & Wang (CoreTech / NTHU). A better check
than case 1 in three ways - it is a lens rather than a plate, the material is a
cyclo-olefin, and its depth data comes from one self-consistent method
(successive 0.1 mm layers turned off, fringe order recounted).

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 4.763e-4 against a published 3.7e-5 - **12.87x** | within a factor of 2 | **FAIL** |
| in-plane shape | maximum at the gate, 14% of it at the far edge | must decay from the gate | PASS |
| layer removal | 3 of 4 cumulative points within 10 points | 3 of 4 | PASS |

### The two open failures, and what is known about each

**The depth profile peaks mid-wall on both cases; the measurements peak at the
skin.** Located 2026-08-18 with the model's own stored intermediates rather than
a recomputation. The memory factor itself peaks at 60% of the half-wall:

| depth | reduced time at freeze | memory | tau (MPa) | dn_flow |
|---|---|---|---|---|
| 100% (wall) | 0.000 | 0.0000 | 1.67 | 1.49e-4 |
| 80% | 1.096 | 0.1603 | 1.34 | 5.54e-4 |
| **60%** | 4.521 | **0.4447** | 1.00 | **9.66e-4** |
| 40% | 9.716 | 0.4156 | 0.67 | 5.88e-4 |
| 0% (core) | 16.989 | 0.2273 | 0.00 | 9.83e-6 |

Memory is the product of two monotone factors running in opposite directions -
build-up needs reduced time, retention is destroyed by it - so the product must
peak somewhere between wall and core. At the wall a layer freezes before it can
build anything; at the core it builds fully and then relaxes.

**The flow channel's depth shape comes from the Lagrangian particle model, and
this is the default since 2026-08-18.** `-eulerian-depth` turns it off and
restores the previous behaviour exactly. It takes the depth SHAPE from `Lagrangian.cs` and applies it to
the Eulerian per-station magnitude, normalised to mean 1 over the wall - so each
station's thickness average is multiplied by 1 and cannot move. Every clause that
reads a thickness average is invariant by construction, asserted at runtime
rather than hoped for; only the depth clauses can respond. In-plane numbers come
back bit-identical either way on both cases.

The shape is solved **per station on the local gap**. It depends on the station
only through the gap, so it is solved at a few gap ratios spanning the part and
interpolated between them, using the same similarity the Eulerian channel already
applies - depths scale with the gap, times with its square. A uniform gap
collapses to a single solve, so the plate case costs exactly what it did.

| | case 1 depth ratio | case 1 peak position | case 2 layer removal |
|---|---|---|---|
| `-eulerian-depth` (was the default) | 0.82 **FAIL** | 53% **FAIL** | 3 of 4 **PASS** |
| Lagrangian, one shape per part | 3.45 PASS | 93% PASS | 2 of 4 **FAIL** |
| **Lagrangian, per-station (default)** | **3.45 PASS** | **93% PASS** | **3 of 4 PASS** |

On the default, **case 1 meets the registered criterion** and case 2 has only its
in-plane peak outstanding.

**It costs time, though much less than it did.** `-selftest` runs in about 1m20
and case 2 in about 19 s, against 2m47 and 36 s when the shape first became the
default. Two things did that. The temperature lookup inside the particle solve
was doing two linear scans - one over the depth grid, one over the time grid - on
every particle on every step, 12 million times; an element's depth node is fixed
until the front deposits it and its clock only moves forward, so both became O(1)
with the answer bit-identical. And an element below Tg is inert but was still
being visited and looked up every remaining step, which the skin pays for
thousands of times over; it is now retired permanently, guarded by a runtime
check that the cooling history really is monotone.

**Where the cost actually is, measured rather than assumed.** At the shipped
nz=41 default, case 1 takes 1.9 s with the shape OFF and 11 s with it on - so the
Eulerian channel and the freeze solve together are under two seconds and the
particle shape is the rest.

The channel is not what scales badly either. Case 1 with the shape off runs in
1.1 / 6.5 / 51 s at nz 41 / 81 / 161 - roughly nz-cubed, which is the EXPLICIT
conduction solve rather than the channel: `dt = 0.2*dz^2/alpha` is a stability
limit, so halving dz quadruples the step count on top of the extra nodes.
Flattening that means an implicit scheme, which would change a validated
component and move the reference numbers, and at nz=41 it costs 1.1 s. Recorded
as measured and deliberately not done.

Shapes are cached between builds, keyed on the CONTENTS of the fill field, freeze
history and the four process fields the solve reads. It is worth less than it
sounds: `-selftest` reuses 6 of 32 requests and case 1 reuses 1 of 6, because
these runs mostly ask for genuinely different things - a mirrored freeze history,
a zero-CTE polymer, freeze times scaled by a probe, one gap node per station. The
cache removes the repeats and there are not many. Keying it on object identity
instead, which was the first version, reused NOTHING at all: each self-test
section builds its own fill field and freeze history even where the geometry is
identical.

The middle row is why the per-station solve exists. A single part-wide shape
makes `DnFlow[i,k] = A_i * phi[k]`, so the normalised depth profile is identical
at every station - right for a plate, wrong for a lens whose gap varies 2.5x.
Case 2's layer-removal clause is evaluated at the 0.8 mm gate region, and a shape
computed on the 2.0 mm centre thickness put 43.0% of the retardance in the first
0.1 mm against a measured 27.9%. On the local gap it reads 32.6%.

**What was swept, and what the sweeps cost.** Grid: phi at the wall 2.268 to
2.326 across nz 41/81/161/321 with the depth ratio flat at 3.43-3.46. Particles:
the first default of 4000 was carrying about 6% - phi goes 2.466 / 2.628 / 2.637
and 6.908 / 6.489 / 6.430 at 4000 / 16000 / 64000 - so the default is now 16000,
where it is converged to ~1%. Gap nodes: at 4000 particles the node count looked
to matter by 11%, and at 16000 that falls to 1-3% and goes non-monotone, so most
of it was particle noise; 6 nodes ships. Both are settable with
`-shape-particles` and `-shape-nodes`. A reference case takes 2-3 minutes with
the shape on.

**Two self-tests had to move, and neither was a numerical regression.** Both
were written for the Eulerian decomposition that the port replaces, so both are
now pinned to `-eulerian-depth`, where the property they assert is true and still
worth guarding. "The fountain is the same at the gate and the far edge" assumed
the fountain is a separable additive term at a fixed depth; under the port it is
folded into the station's thickness average and redistributed by a shape. "Shear
birefringence vanishes at the mid-plane" holds because an Eulerian element there
has sat at zero shear stress since t=0 - give the material a path and the element
now at the mid-plane arrived from somewhere with nonzero shear, so it carries
orientation. Asserting that on the default path would be asserting the assumption
the port exists to remove.

The default path gained two checks of its own: the port must leave the thickness
average alone (it matches to 1.2e-16) and it must actually move the skin value
(7.37e-5 -> 8.45e-4), so the first cannot pass on a port that did nothing. 57
self-tests pass, 0 fail.

**One clause cannot test the interpolation, and says so.** Case 2's layer removal
samples s = 0, which is the minimum gap and therefore the first interpolation
node exactly - so it returns identical numbers for every node count by
construction. The per-station phi rows printed beside it are what actually
exercise the interpolation.

**The underlying Eulerian defect is still not a term that can be corrected.** It is what an Eulerian channel
computes when it assumes every layer sat at its final depth since t=0. The
measured profile peaks at the skin because the skin's orientation was never
built locally - it was sheared in the hot core, carried to the wall by the
advancing front and quenched on arrival (Mavridis, Hrymak & Vlachopoulos,
*J. Rheol.* **32**(6) 639, 1988). `Lagrangian.cs` carries that history properly
and is not yet the shipped path.

Eight configurations of additional terms have been measured against this and
rejected; tripling the deposition term raises the wall from 1.49e-4 to 4.47e-4
and **leaves the peak at 60%**. The log records each one so they are not
proposed again.

**Case 2's 12.87x is a model error, not a bad input - but its magnitude clause
is not currently a clean test.** Every material and process input has been
sourced or shown to be a safe borrowing, and each correction made the gap
larger. The one registered falsifier - that ZEONEX 480R might have a melt
stress-optical coefficient an eighth of the borrowed value - **is dead**: Inoue
et al., [*Polymer Journal* (1995)](https://www.nature.com/articles/pj1995122),
measure ~1700 Br for amorphous polyolefins and state it is insensitive to
molecular structure, with ROMP cyclic olefins at the *high* end. Zeon's
low-birefringence claim is about the **photoelastic** constant, which governs the
thermal channel; the two coefficients are three orders apart.

The caution: the model **over-retains** rather than over-stresses (case 1
retention 0.235 against a measured 0.202; case 2 0.143 against 0.0111), and the
wall shear stress it computes, 1.67 MPa, is above the melt-fracture threshold.
Both inputs that set it - the cavity's share of the shot, and a 12.6 mm flow
width - are unsourced choices.


---

# The four reference cases, in detail

**MOVED OUT OF THE README 2026-08-20**, unchanged, when that file was cut in
half. The README keeps the verdict table and the honest-use envelope; this is
the working detail behind them. Nothing here was rewritten in the move - if a
number below disagrees with the README, the README is newer.

#### Case 1 - TOPAS 6017S-04 plate

100 x 100 x 1.5 mm, film gate on one edge, 280 C / 150 C / 71.3 MPa. Constants
from Kim, Yoon & Kornfield, *Key Eng. Mater.* **326-328** (2006) 183. Polymers
2024 16(2) 168.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 1.392e-4 against a published 1.2e-4 - **1.16x** | within 2x | PASS |
| in-plane shape | maximum at the gate, 47.3% of it at the far edge | must decay | PASS |
| gate null | peak moves x=0 -> x=100 mm when the gate moves | must track | PASS |
| depth ratio | **3.44** against a published 2.78 | [1.39, 5.56] | PASS |
| depth peak | maximum at 95% of the half-wall | beyond 75% | PASS |
| depth null (flow) | mirrored freeze order drives the deep value to exactly 0 | must respond | PASS |
| depth null (thermal) | CTE=0 collapses to flow-only, channel material (3.44 vs 2.84) | must collapse | PASS |

Converged at **nz=41**: depth ratio 3.43 / 3.46 / 3.45 / 3.43 and peak 1.16x /
1.16x / 1.16x / 1.17x at nz 41 / 81 / 161 / 321. nz=21 is NOT converged, so 41 is
a floor rather than the smallest grid that passed.

**Channel split, printed beside the clause because a ratio built from a SIGNED sum
cannot be attributed from the ratio alone:** at the surface sampling depth flow is
3.169e-4 and thermal +4.42e-5 (14%); at the deep point 1.116e-4 and -6.66e-6 (6%).
That brackets the 8% thermal share published for this material class. It was 26%
until the post-vitrification increment was restricted to free parts - see below.

#### Case 2 - ZEONEX 480R plano-convex lens

32 mm diameter, 2 mm centre thickness, 0.8 mm edge gate, 275 C / 124 C, 98.10 MPa.
Chang, Yu, Chiu, Yang, Lai & Wang (CoreTech / NTHU). Chosen because it is a lens
rather than a plate, a different material family, and its depth data comes from
successive 0.1 mm layers turned off with the fringe order recounted - so the
quantity removed IS the quantity measured.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 4.434e-4 against a published **3.68e-3** - **0.12x** | within 2x | **FAIL (LOW)** |
| in-plane shape | maximum at the gate, 14% of it at the far edge | must decay | PASS |
| layer removal | 32.4 / 44.7 / 48.9 / 50.0% against 27.9 / 30.8 / 43.9 / 46.2% | 3 of 4 within 10 pts | PASS |

**The registered reference was wrong by a factor of 100, and correcting it
INVERTS this failure.** Chang et al. Fig. 7's y-axis is labelled x10^-5 and its
peak reads 3.7 - the source of the old 3.7e-5. That label is a typo. The same
paper gives dn = lambda*N/h, states a maximum observed fringe count of N = 5, and
has a 0.80 mm gate, so 589.3e-9 x 5 / 0.8e-3 = **3.68e-3** - matching the plotted
3.7 to two significant figures. Two cross-checks from the companion paper agree
in order: 6.5 fringes over the 2 mm centre is 1.9e-3, and its removal axis runs
to 2.21e-3. Nothing in either paper supports 1e-5.

The model reads 4.434e-4, so it is about **8x LOW** where it had been recorded as
12x HIGH. **The correction does not rescue the clause - it still fails, in the
other direction - which is why it can be trusted.** Withdrawn along with the old
direction: "the model over-predicts"; the suspicion that a melt-fracture-level
wall shear stress was CAUSING an over-prediction; and the reading that 480R's
borrowed melt coefficient being larger than 1000 Br would make matters worse. It
would now help.

**The fill time is now sourced** at 0.50 s (Lai & Wang Fig. 5c), replacing a
0.109 s derivation that assumed the whole screw output entered this one cavity.
tau_wall falls 1.67 -> 1.05 MPa, within ~20% of that paper's own simulated
0.75-0.89 MPa peak, so the melt-fracture concern largely dissolves on sourced
numbers. The gate width remains a choice of mine.

**The sampling thickness in the reference is pinned, not open.** Eq. (9)'s text
calls Fig. 7 the "gap wise average residual birefringence" - the same quantity
this model averages - and solving that equation for the thickness each axis
reading would require settles both at once, assuming nothing: the x10^-5 label
needs h = 79.6 mm, forty times the lens's centre thickness, while x10^-3 needs
0.796 mm, the 0.80 mm gate land to within 0.5%. So the 5x deficit is real and
none of the five candidates explains it - not the flow inputs, the relaxation
time, the retained fraction, the depth port's normalisation, or the conversion
thickness.

#### Case 3 - free quench, the THERMAL channel alone

Bisphenol-A polycarbonate, 2 mm sheet, 160 C -> 60 C bath. Wimberger-Friedl, PhD
thesis, TU Eindhoven (1991) ch. 3.2, open access. Cases 1 and 2 are mouldings, so
every number in them is flow and thermal together; this channel had only ever
been tested by NULLING it. A quench has no flow, and the thermal construction
reads only the freeze history - so no gate, flow rate or fill time reaches it.

| Clause | Result | Bar | |
|---|---|---|---|
| sign reversal | core +5.38e-4, surface -1.42e-3 | must reverse | PASS |
| direction | core tension, surface compression | as published | PASS |
| zero crossing | z/d **0.649** (published 0.5-0.8) | [0.40, 0.90] | PASS |
| shape ratio | \|surface\|/\|core\| **2.64** (published 1.7-4.0) | [1.0, 8.0] | PASS |
| magnitude | \|surface\| 1.42e-3 against a published 1.75e-3 | within 3x | PASS |
| null | CTE=0 collapses the profile to exactly 0 | must collapse | PASS |
| control on the null | CTE restored gives 3.20e-3 | must not be dead | PASS |

Published figures are read off scanned figure axes at +-10-15%, so every band is
set wide for that. The magnitude clause was registered as deliberately weak
BEFORE running, because the source attributes much of the quench birefringence to
frozen-in orientation above Tg, which this channel does not model.

**All seven passed first run, and the case immediately found a failure they
cannot see.** The source reports the zero crossing moving OUTWARD as initial
temperature rises, z/d ~0.3 to ~0.85, naming Ti the dominant control. The model
moves it 0.645 -> 0.656 - right direction, span 51x too small. Printed as an
unscored trend diagnostic rather than folded into the verdict, because
registering a clause after seeing its result is moving the bar.

#### Case 4 - moulded PC plate, with the channels separated by the author

80 x 35 x 2 mm polycarbonate plate, melt 320 C, mould 30-120 C, 25.4 cm3/s, no
packing stage. Wimberger-Friedl (1991) ch. 3.3 - the injection-moulding half of
the same open-access thesis that supplies case 3.

**This is the first case that can test the SPLIT between the two channels**, and
it can because the author took the part apart: residual stress by layer removal
(*not exceeding 1 MPa*, below 1e-4), frozen-in thermal orientation (5e-4 average,
*more than twice* the flow contribution), and flow orientation at the surface,
attributed to elongation at the melt front. This model has the first and the
third and not the second, so its like-for-like counterpart is the 1 MPa bound and
NOT the 5e-4 plateau.

The fill time is sourced twice and the two agree: 5600 mm3 at 25.4 cm3/s is
0.220 s, and the measured cavity-pressure trace peaks at about 0.35 s.

| Clause | Result | Bar | |
|---|---|---|---|
| thermal stress bound | peak **0.000 MPa** (was 7.44 before the fix below) | <= 3 MPa | PASS |
| thermal dn bound | peak **1.7e-11** (was 5.36e-4) | <= 3e-4 | PASS |
| total magnitude | 3.43e-4 against a measured 6.0e-4, ratio **0.57** | [0.15, 1.00] | PASS |
| flow peak position | \|z/d\| **0.883** (published ~0.95) | >= 0.70 | PASS |
| Tm trend, peak moves out | 0.878 -> 0.883 -> 0.888 at 30/60/90 C | must rise | PASS |
| Tm trend, stress rises as Tm falls | both arms below 1e-6 MPa - **vacuous** | must fall | PASS |
| null | CTE=0 collapses the thermal stress to exactly 0 | must collapse | PASS |
| control on the null | CTE restored gives 2.4e-7 MPa | must not be dead | PASS |

The total-magnitude clause is **one-sided on purpose**: over-predicting is a
FAILURE here, which no other case in this project does. The model is missing the
mechanism the source says supplies more than half of what the instrument sees, so
reaching the measurement would mean compensating with the wrong physics rather
than agreeing. 0.67 and below is the direction that makes sense.

**On its first run this case failed those first two clauses at 7.44 MPa, and
that failure is why the boundary condition below was rewritten.** The thermal
channel was over-predicting residual stress in a moulding by three to eight
times, because it imposed free-plate force and moment balance at every increment
while the part was still adhered to the cavity.

#### The thermal boundary condition - adhered, then released

The source states the physics directly (p. 130): with wall adhesion *"stresses
are not equilibrated within the polymer ... When the polymer is released, the
tensile stresses will be relieved so that no residual stresses remain"*. So a
moulding is two lines of physics, not one:

1. **Held.** A layer vitrifies stress-free at Tg and cools with its in-plane
   dimension pinned by the steel, accumulating `E/(1-nu) * alpha * (Tg - T_k)`
   with no redistribution. Every layer starts from the same Tg, so this is
   **path-independent** - the cooling history never enters.
2. **Released.** The constraint goes and the free part must carry no net force
   and no net moment, so the balancing `(a + b*z)` is subtracted over the whole
   thickness.

Tg is common to every layer, so it cancels. **What survives is the temperature
non-uniformity at release, and nothing else.** Hold a part until it is thermally
uniform and the residual thermal stress is exactly zero.

That also rehabilitates a branch refuted earlier in this arc. A
constrained-then-released form was tried, returned identically zero, and was
discarded on that basis - but it had been applied to the POST-vitrification
increment, where every layer does cool from the same Tg to the same wall and zero
is genuinely uninformative. On the during-solidification stage, where the stress
is actually built, it is the whole mechanism. The measured answer is "not
exceeding 1 MPa", which on a scale where free-plate gives 7.44 is zero to within
the instrument.

**Three of the eight clauses now pass without discriminating anything**, and the
output says so rather than counting them quietly: the two bounds cannot tell a
right answer from a dead channel when the model predicts zero, and the Tm stress
trend resolves on floating-point noise. **The evidence is the release-time sweep
instead**, which tests the construction's own prediction:

| release, s | core-skin dT | peak sigma_th |
|---|---|---|
| 4.24 (at the freeze front) | 83.3 C | 12.905 MPa |
| 7.24 | 31.8 C | 4.929 MPa |
| 14.24 | 3.4 C | 0.522 MPa |
| 59.78 (registered) | 0.0 C | 0.000 MPa |

A 2 mm plate held 60 s against a ~3 s thermal time constant *is* uniform at
release, so the zero is a prediction about this part, not a property of the
construction. Eject it hot and the stress is there.

**It is corroborated on a case it was not built for.** Case 1 - different
polymer, different part - has a published depth ratio of 2.78. Free plate gives
3.43; adhered gives **2.84**. The old construction was contributing a spurious
0.6 to that ratio.

**It is not yet the default for cases 1 and 2.** Adhesion takes case 1's elastic
thermal channel to 0% of flow, and case 1 carries a registered control asserting
that channel is *material*, which then fails. That control was registered under
the free-plate construction and needs re-registering against the orientational
channel this model still lacks - rewriting it now, to make a case pass, is the
one thing this project does not do. Cases 1 and 2 keep the old construction
behind `-adhered` until that is settled.

Wiring that up also found a **dead guard**: case 1's thermal-null clause was
computed, printed as PASS/FAIL, and never read by the verdict. No past verdict
was wrong - it passes in the shipped configuration - but it could have printed
FAIL beside a MET. It is now wired, and verified in both directions.

Two numerical defects, handled differently and deliberately. **The boundary node
is an artefact and is excluded**: it read -66 MPa, and refining the grid settles
which it is - -25.4 / -53.1 / -66.0 MPa at nz 41 / 81 / 161 while the interior
converges to 2.8. A physical stress converges. Case 3 already excludes the same
node for the same stated reason. **The peak statistic is grid-noisy and is NOT
fixed**: the clause reads 3.07 / 10.42 / 7.44 MPa across the same grids, and a
grid-robust interior companion is printed beside it and explicitly not scored,
because changing a clause after watching it fail is moving the bar.

The plate is rectangular and this solver's cavity is a disc, so the case uses the
equal-VOLUME disc: Q and the fill time come out at the sourced values exactly and
the path length carries the error (59.7 mm against 80). `-semidia 40` runs the
alternative that gets the length right and Q 1.8x too high.

#### The thermal channel, as of 2026-08-18

**Stress accumulates INCREMENTALLY** - a layer is stress-free above Tg, becomes
elastic when it vitrifies, and every later cooling increment re-equilibrates over
the layers solid at that moment, so the total is force- and moment-balanced
without imposing it. The previous construction read the temperature profile at
one instant and removed its mean and linear parts; that is capped, since the
profile then is near a similarity solution and its crossing cannot move. On case
3 the incremental form took the surface from -9.5e-4 to -1.42e-3 and the ratio
from 1.76 to 2.64. `-snapshot` restores the old one.

**The post-vitrification increment is for FREE parts only.** Completing the
cooling after every layer is solid is essential to a quench - case 3's core reads
7.6e-7 without it - and wrong for a moulding, where the part is still adhered to
the cavity and cannot relieve in-plane, so the denied contraction is identical
for every layer and cancels at ejection. Including it put case 1's thermal share
at 26% of flow against a published 8%; excluding it gives 14%.

That cancellation was implemented as a constrained-then-released branch and
MEASURED: it returns identically zero. ~~which refuted it as a general
construction~~ - **that conclusion is retracted, 2026-08-19.** Zero was read as
absurd; case 4 measures the published answer as "not exceeding 1 MPa", which on
the scale where free-plate gives 7.44 is zero to within the instrument. The
branch had been tried only on the POST-vitrification increment, where every layer
does cool from the same Tg to the same wall and zero is genuinely uninformative.
On the during-solidification stage it is the correct mechanism - see the thermal
boundary condition section above. What survives unchanged is the narrower claim
this justified: the post-vitrification increment is excluded for a moulding.

**Cooling after EJECTION contributes exactly zero, and that is a proof.** The
part is then entirely glassy - nothing vitrifies, no sub-Tg relaxation, one
modulus - and a linear-elastic body taken through a cycle that starts and ends
uniform has no residual stress. The condition is mould < Tg, which this tool
already ENFORCES (`FreezeHistory` refuses anything else), so the result is
unconditional rather than a property of the cases run. A warning for the
mould-above-Tg case was written and removed as dead code: with case 1 set to a
200 C mould it never printed, because the run fails earlier. The self-test
asserts the precondition instead, in both directions.


---

# How the channels are built, and the catalog prerequisite

**MOVED OUT OF THE README 2026-08-20**, unchanged, in the same cut. These are
the mechanism notes - the polymer catalog STAR needs, the Lagrangian depth
construction, and the fountain term - which belong with the working detail
rather than in a front page.

HOW THE CHANNELS ARE BUILT

**A polymer catalog is a prerequisite, not an extra.** No polymer OpticStudio
ships carries a `BD` record: across all 51 installed catalogs there are 578 of
them and every one is on a glass. Without it STAR does not refuse the stress data
through the ZOS-API — it accepts zero points, returns success, and reports
retardance exactly zero, which is indistinguishable from a well-moulded part.
`-writecatalog` writes the missing constants; they are marked PROVISIONAL
everywhere because they are representative of the polymer family rather than
measured for a grade.

Two behaviours worth knowing, both measured rather than documented anywhere:
`DirectIndex` and `Stress` are **mutually exclusive per surface** (loading the
index onto a stressed surface silently empties the retardance map), which is why
the density term rides in the stress tensor as a hydrostatic component; and
`GetRetardanceMap`'s first argument is a sampling selector, not a point count.

**The depth distribution of the flow channel comes from a Lagrangian particle
model, and that is the default.** The Eulerian channel computes, for each depth,
the stress an element would build up having sat there since t=0 - and under that
assumption the retained orientation must peak between wall and core, because
build-up and retention run in opposite directions in reduced time. Measurements
peak at the skin, because in fountain flow the skin never sat at the wall: it was
sheared in the hot core, carried to the wall by the advancing front and quenched
on arrival. The particle model carries that history; the shape it produces is
solved per station on the local gap and applied to the Eulerian per-station
magnitude, normalised so that no thickness-averaged quantity can move. It costs
tens of seconds per build. `-eulerian-depth` turns it off.

**Fountain flow is ON by default.** Material reaching the cavity wall got there
through the melt front, turning through roughly a right angle and stretching on
the way; that strain is imposed once at deposition and then relaxes, so the skin
keeps nearly all of it and the core loses nearly all of it. Its magnitude comes
from the front kinematics — a Maxwell fluid extended at v_front/(h/2) for one
gap-crossing time — not from a chosen strain.

It was gated off for part of its history, because enabling it then made both
criteria worse. The viscosity-weighted shear rate inverted that: shear alone now
correctly gives a fast-freezing skin almost no orientation, so **deposition at the
front is the only thing left that can orient one**. With both channels the
in-plane peak goes from 0.62× to 1.16× of the published value and the depth ratio
from 0.31 to 0.82, on measured constants with no fitted parameter between them.
(Re-measured 2026-08-18 at `-nz 161`; the pair quoted here before the
freeze-history fix, 0.26×/0.90× and 0.02/0.76, was taken on the bad grid.)
Disable with `-fountain 0` to recover the shear-only model.
