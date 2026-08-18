# Validation sources for MoldStress

The model currently rests on **one** reference case (Polymers 2024, 16(2), 168 —
a TOPAS 6017 plate), and that reference has two known weaknesses: its headline
ratio had to be withdrawn because it was formed across two instruments, and its
depth profile is read off 0.2 mm slabs whose sampling definition the paper does
not state. One reference with two open questions is thin for a tool meant to
ship. This file collects candidates, with what each can and cannot settle.

Ranked by usefulness, not by how easy they were to find.

---

## 1. Chang, Yu, Chiu, Yang, Lai & Wang — *Simulations and Verifications of True 3D Optical Parts by Injection Molding Process*

CoreTech System (Moldex3D) + National Tsing Hua University.
<https://www.moldex3d.com/assets/2011/09/SMGG5H7.pdf> — open, read in full.

**This is the best available check and it is better than the current reference
case in three ways: it is a LENS not a plate, the material is a cyclo-olefin, and
its depth data comes from ONE self-consistent method.**

| | |
|---|---|
| part | plano-convex spherical lens, diameter 32 mm, thickness 2 mm, gate thickness 0.8 mm, edge gate on the Y axis |
| material | **ZEONEX 480R** — a COP, same family as the COC the model has measured constants for |
| melt / mould | **275 °C / 124 °C** |
| holding pressure | 98.10 MPa |
| injection speed | 22 mm/s |
| cooling time | 60 s, ejection at 127 °C |

### What it can settle

**(a) The in-plane clause, directly.** Fig. 7 plots residual birefringence
measured along the filling path, **stated measurement error < 10%**:

| distance from gate | birefringence |
|---|---|
| 0 mm | ~3.7 × 10⁻⁵ |
| 3 mm | ~2.5 × 10⁻⁵ |
| 6 mm | ~1 × 10⁻⁵ |
| 12 mm | ~0.2 × 10⁻⁵ |
| ~24 mm | small secondary rise, ~0.4 × 10⁻⁵ |
| 30 mm | ~0 |

Peaks at the gate and decays to nothing — the shape the current model gets
wrong under every envelope configuration, now measurable on a second part.

**(b) The depth clause, by LAYER REMOVAL — one method, no cross-instrument
ratio.** Successive 0.1 mm layers were diamond-turned off and the fringe order
recounted (Table 2):

| removed | fringe order removed |
|---|---|
| 0.1 mm | **27.9%** |
| 0.2 mm | 30.8% |
| 0.3 mm | 43.9% |
| 0.4 mm | **46.2%** |

So the outer **0.1 mm of a 2 mm part — 5% of the thickness — carries 28% of the
retardance**, and the outer 0.4 mm carries 46%. That is a cumulative
distribution, which is a far stronger constraint than a two-point ratio and has
no sampling ambiguity: the quantity removed is the quantity measured.

**(c) The channel split.** The paper states flow-induced birefringence accounts
for **92%** against **8%** thermal for COC moulded lenses, citing Wang & Lai.
That is a direct check on the model's two-channel decomposition, and it is
consistent with the finding that a hot mould produces little thermal stress.

**(d) A second peak.** Both the simulation and the layer-removal experiment show
birefringence peaking near the wall AND again near the mid-thickness, the second
peak appearing between 0.2 and 0.3 mm of removal and attributed to packing flow
through the narrowing channel.

### What it cannot settle, and one caution

It reports **fringe order**, converted via `Δn = λN/h` at 589.3 nm — a
**gapwise-average**, not a point value, so it constrains the integral rather than
the profile shape directly. And **the paper's own simulation under-predicts**:
maximum fringe count 5 observed against 4 predicted. A model agreeing with
Moldex3D here is agreeing with something already known to be ~20% low.

**Internal inconsistency to be aware of:** the body text says the lens curvature
is 70 mm; Fig. 1 labels it **75 mm**. Take the geometry from whichever the
comparison is sensitive to, and say which was used.

---

## 2. Isayev et al. — residual stress and birefringence in moulded PC and PS

*Residual stresses and birefringence in injection molding of amorphous polymers:
simulation and comparison with experiment*, J. Polym. Sci. B (2006);
gapwise distributions measured with a polarising microscope at five radial
locations on centre-gated discs.

The classic dataset, and the one that establishes the flow/thermal decomposition
the model uses. **Paywalled** — characterised here from abstracts only, so it is
a lead to obtain, not a source that has been read.

## 3. Wimberger-Friedl — optical assessment of orientation, stress and density

*Prog. Polym. Sci.* (1994), a review of what is actually measurable in moulded
amorphous polymers and how the contributions separate. Paywalled.

## 4. Handbook of Plastic Optics, ch. 4 — *Metrology of Injection Molded Optics*

Wiley. Would establish what magnitudes are normal and what instruments resolve —
useful for judging whether a predicted 241 nm retardance is remarkable or
routine. Paywalled.

## 5. Wang & Lai — *Study of Residual Birefringence in Injection Molded Lenses*

ANTEC 2007, pp. 2494–2498, and the 2008 follow-up *Experimental Verifications of
CAE Predictions on Birefringence of Injection Molded Lenses*, ANTEC 2008,
pp. 421–425. These are the primary source behind the 92%/8% split quoted above.
Worth obtaining, since source 1 cites rather than reproduces them.

---

## What to do with these

The layer-removal cumulative distribution in source 1 is the single most useful
datum, because it is immune to the sampling question that still hangs over the
current reference case. Reproducing it needs the model to integrate its own
profile over successive outer layers and report the cumulative fraction — which
neither the Eulerian nor the Lagrangian path currently does, and which is a
better-posed target than the two-point ratio either is being judged on.
