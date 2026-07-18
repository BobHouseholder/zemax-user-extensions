# CryoGlass v1 — validation record

2026-07-18. Full validation of the CryoGlass extension (Si + Ge, CHARMS
temperature-dependent Sellmeier data) against every independent source
available: the source papers' complete published tables, the
refractiveindex.info database (H.H. Li 1980 compilation), OpticStudio's own
built-in infrared catalog, and the index OpticStudio actually traces.

## A. Against the source papers (Frey, Leviton & Madison, SPIE 6273, 2006)

Every extracted value of every published table, checked against the evaluator
(coefficients parsed from the shipped source — single point of truth, no
re-transcription):

| dataset | points | worst deviation | verdict |
|---|---|---|---|
| Si measured index (Table 2) | 96 | 6.9e-6 | PASS |
| Ge measured index (Table 7) | 96 | 8.4e-6 | PASS |
| Si dispersion dn/dλ (Table 3) | 36 | 6.8e-4 µm⁻¹ | PASS |
| Ge dispersion dn/dλ (Table 8) | 36 | 7.0e-4 µm⁻¹ | PASS |
| Si thermo-optic dn/dT (Table 4) | 72 | 2.4e-5 K⁻¹ | characterized* |
| Ge thermo-optic dn/dT (Table 9) | 60 | 3.8e-5 K⁻¹ | characterized* |

*The dn/dT tables are *measured* thermo-optic data, while CryoGlass
differentiates the *fitted* index model; the model's derivative tracks the
measurements to typically <1e-5 K⁻¹, with the largest deviations at the fit
edge (295 K) and at a step feature visible in the published 80–100 K Ge
column. Over a ±10 K span this bounds induced index error at ~4e-4 —
consistent with the fits' stated ~1e-4 index residual, and the reason
CryoGlass regenerates catalogs per working temperature instead of trusting
any wide-range dn/dT model.

## B. Against independent index sources

**H.H. Li 1980 compilation** (via the refractiveindex.info database,
tabulated n): the first quantified CHARMS-vs-Li comparison at cryogenic
temperature we are aware of — the two authoritative sources genuinely
disagree at cryo, and the disagreement matters at design accuracy:

| case | MWIR plateau Δ (CHARMS − Li) | band-edge Δ |
|---|---|---|
| Ge @ 100 K | +4.1e-3 | +1.2e-2 @ 2.0 µm |
| Ge @ 293 K | +0.8…1.9e-3 | +8.0e-3 @ 2.0 µm |
| Si @ 100 K | +3.0e-3 | +9.4e-3 @ 1.2 µm |
| Si @ 293 K | +2.2…2.9e-3 | +5.5e-3 @ 1.2 µm |

CHARMS is a direct cryogenic measurement (±1e-4 class); Li is a
literature compilation whose cryo coverage leans on extrapolation. The
source paper itself notes inter-source spreads exceed stated uncertainties
(interspecimen variability). Choose knowingly.

**OpticStudio built-in INFRARED catalog** (in-trace, both converted to
absolute at 293 K): agreement to 3–4e-4 for Si at all test wavelengths and
+3.8e-4 … −1.3e-3 for Ge — well inside the published inter-source spread,
and much closer to CHARMS than Li is.

## C. In the trace (INDX operand, fresh OpticStudio instance)

Catalogs generated at 50 / 100 / 150 / 200 / 295 K, environment set to each
working temperature at 0 atm, index read back through the ZOS-API:

- 30 sweep points (2.5/4.0/5.0 µm × Si/Ge × 5 temperatures): worst
  |traced − CHARMS| = **2.0e-4**, occurring at the 50 K extreme;
  **≤1.2e-4 at 100 K and above** (at 100 K: worst 9.9e-5 — inside the
  dataset's own ±1e-4 uncertainty class). The 50 K residual is a
  characterized difference between CryoGlass's air model and OpticStudio's
  at extreme cold-air density, and shrinks with temperature.
- Local Schott thermal model exercised at 120 K from the 100 K catalog:
  worst 1.4e-4, matching the per-glass fit error the tool prints.
- Benchmark lens: Ge plano-convex singlet (r = 296 mm) at 100 K traces,
  focuses, and yields EFFL = 100.0325 vs the r/(n−1) prediction 100.0337 —
  agreement to 1.2e-3 of theory; on-axis RMS spot 2.8 µm (spherical-
  dominated f/10 singlet, as expected).

During development this in-trace stage caught the one real defect: catalog
data is interpreted by OpticStudio as relative to air at the reference
temperature at 1 atm, and absolute coefficients traced ~n_air(100 K) ≈
3.3e-3 high — invisible to every out-of-trace test. The conversion is now
analytic (Sellmeier5 with a vanishing-resonance constant term) with the
true residual printed per glass at generation time.

## D. Refusals

- T outside a material's measured range (e.g. 10 K): refused by name.
- λ outside the measured band is outside the emitted LD limits; CHARMS
  stops at ~5.6 µm — **LWIR is not covered** and the tool never
  extrapolates.
- Unknown material: warned, with the available list.
- The built-in self-test (paper anchors) runs before every generation and
  refuses to generate on disagreement.

## Known limitations

- TCE = 0 in generated catalogs (CHARMS is index-only); source thermal
  expansion separately before thermal-mechanical analyses.
- Catalogs are per-working-temperature snapshots; the embedded Schott
  thermal model is a local fit (±25 K box, error printed) — regenerate
  rather than extrapolate.
- Indices are vacuum-referenced: run with the environment at the working
  temperature and 0 atm.
- At 50 K, in-trace agreement degrades to 2.0e-4 (air-model variant).

## Sources

- Frey, Leviton & Madison, Proc. SPIE 6273, 62732J (2006) — NTRS 20070021411
- H.H. Li, J. Phys. Chem. Ref. Data 9, 561 (1980), via the
  refractiveindex.info database (CC0)
- OpticStudio INFRARED.AGF built-in catalog (Ansys Zemax OpticStudio 2026 R1.01)
- Frey & Leviton, Proc. SPIE 5494 (2004) — the CHARMS facility
