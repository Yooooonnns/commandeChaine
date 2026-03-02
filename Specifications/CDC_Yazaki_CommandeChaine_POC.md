# Yazaki Commande Chaine
## Technical and Functional Specification (POC Phase M1)

## 1. Executive Summary

This specification defines the target architecture and functional behavior for automated line-speed control on the Yazaki wiring preparation line. The objective is to replace manual-only cadence adjustment with a controlled Auto mode driven by calculated cycle time, while preserving Manual mode as an independent safety and operations fallback.

The solution is based on:
- IT-side cycle-time computation in the .NET API
- MQTT command distribution to edge control
- Raspberry Pi edge conversion from CT to speed/voltage
- Industrial DAC output (0–10V) toward NORDAC 500E
- Real-time supervision in the desktop dashboard

The document is written for management review, maintenance preparation, and industrial validation.

## 2. Business Context and Motivation

### 2.1 Current Operating Reality

The line currently relies on manual potentiometer adjustment on the NORDAC 500E. This produces variability between shifts and operators, limited traceability, and no direct IT-to-OT control loop.

### 2.2 Target Outcomes

| Objective | Operational Effect |
|---|---|
| Stabilize cadence | Repeatable line speed aligned with workload |
| Improve traceability | Historical records of CT, speed, mode, and events |
| Reduce manual intervention | Automated adjustment during normal operation |
| Preserve safety control | Hardware Manual mode remains always available |
| Prepare digital evolution | Foundation for digital twin and optimization |

## 3. Scope Definition

### 3.1 In-Scope (POC M1)

| Domain | Included Capability |
|---|---|
| IT Logic | CT calculation based on production context |
| Messaging | MQTT publish/subscribe command loop |
| Edge Control | Filtering, ramping, clamping, timeout fallback |
| Actuation | DAC command path to NORDAC analog input |
| Supervision | Live dashboard updates via SignalR |
| Operations | Manual/Auto operating model and logging |

### 3.2 Out of Scope (POC M1)

| Domain | Excluded Capability |
|---|---|
| MES/ERP | Full enterprise integration |
| Advanced AI | Predictive control and optimization AI |
| PLC Migration | Replacement of existing safety architecture |
| Energy Program | Full energy optimization layer |

## 4. Target System Architecture

### 4.1 System Context Diagram

This section contains the system context architecture diagram used for IT/OT integration review.

### 4.2 Logical Architecture

| Layer | Component | Responsibility |
|---|---|---|
| IT Layer | .NET API | Compute CT, manage FO/batch state, publish MQTT payloads |
| Messaging Layer | MQTT Broker | Reliable command transport and topic routing |
| Edge Layer | Raspberry Pi | Validate payload, filter, convert CT to voltage command |
| Analog Layer | DAC | Output 0–10V control signal |
| Drive Layer | NORDAC 500E | Convert analog input to motor frequency |
| Supervision Layer | WPF Desktop | Display CT/speed status and operational events |

### 4.3 Physical Integration Overview

| Link | Interface | Key Constraint |
|---|---|---|
| API → Broker | TCP/IP MQTT | QoS 1, timeout handling |
| Broker → Raspberry Pi | MQTT subscription | Payload validation and freshness control |
| Raspberry Pi → DAC | RS485 or I2C | Industrial noise immunity |
| DAC → NORDAC | Analog 0–10V | Shielding, single-point grounding |
| Selector Switch | Manual/Auto hardware path | Manual mode independent from software |

### 4.4 Control Strategy

The core transformation is:

$$
Speed = \frac{PitchDistance}{CT}, \quad Frequency = \frac{Speed}{BeltFactor}, \quad Voltage = Frequency \times \frac{10}{50}
$$

Control constraints:
- Moving average filter on CT input
- Slew-rate limit on voltage variation
- Hard clamping between MIN_VOLTAGE and MAX_VOLTAGE
- Timeout fallback when message freshness is violated

## 5. Functional Scope

### 5.1 Use-Case Diagram Set

This section contains the use-case diagram for IT actors, operators, and maintenance roles.

### 5.2 Auto Mode Workflow

| Step | Action | Output |
|---|---|---|
| 1 | Operator selects AUTO | Auto path enabled |
| 2 | API computes CT | CT command prepared |
| 3 | API publishes MQTT payload | Edge receives command |
| 4 | Raspberry Pi processes CT | Filtered target voltage |
| 5 | DAC outputs voltage | NORDAC updates frequency |
| 6 | Dashboard updates via SignalR | Live supervision and traceability |

### 5.3 Manual Mode Workflow

| Step | Action | Output |
|---|---|---|
| 1 | Operator selects MANUAL | Potentiometer path active |
| 2 | Manual voltage command | NORDAC follows local control |
| 3 | IT supervision remains active | Progress visible, no auto actuation |

### 5.4 Engineering and Supervision Functions

| Function | Description |
|---|---|
| Parameter management | Pitch, belt factor, clamp limits, timeout, filter window |
| Runtime diagnostics | Connectivity, fallback state, command freshness |
| Event history | Mode changes, command updates, critical events |
| Export | Batch-level and trend-level operational traces |

## 6. End-to-End Workflow

This section contains the end-to-end process workflow diagram from CT calculation to VFD actuation.

## 7. Implementation Status and Roadmap

### 7.1 Current Status

| Component | Status | Notes |
|---|---|---|
| API CT Logic | Complete | CT computation and publication implemented |
| Desktop Supervision | Complete | Live updates and chain tracking implemented |
| MQTT Publisher | Complete | Topic publication active |
| MQTT Subscriber | Partial | Depends on broker reachability |
| Raspberry Control | In Progress | Edge algorithms prepared, integration pending |
| DAC Hardware Loop | Pending | Hardware selection and bench validation pending |
| NORDAC Integration | Pending | On-site commissioning required |

### 7.2 Integration Roadmap

| Phase | Window | Deliverable |
|---|---|---|
| Connectivity Stabilization | Week 1–2 | Broker access and end-to-end command delivery |
| Edge + DAC Bench Validation | Week 3–4 | Verified 0–10V generation and frequency response |
| On-Site Commissioning | Week 5–6 | Manual/Auto wiring and line calibration |
| Pilot Operation | Week 7–8 | Supervised production run and tuning |

## 8. Risk Assessment and Mitigation

### 8.1 Technical Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| T1 | Analog noise on 0–10V path | Medium | High | Shielding, grounding, filtered input, bench measurement |
| T2 | MQTT interruption | Medium | Medium | Timeout fallback, heartbeat monitoring, clear alarms |
| T3 | Edge runtime failure | Low | High | Watchdog, health checks, rapid swap procedure |
| T4 | Parameter misconfiguration | Low | High | Validation rules, bounded ranges, commissioning checklist |

### 8.2 Operational Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| O1 | Incorrect mode usage | Medium | Medium | Clear indicators and operator SOP |
| O2 | Insufficient training | Medium | Medium | Structured onboarding and refresher sessions |
| O3 | Unexpected line stop perception | Low | High | Fallback behavior definition and operator guidance |

### 8.3 Safety Principles

| Principle | Implementation Rule |
|---|---|
| Manual priority | Manual mode must remain hardware-independent from software state |
| Emergency isolation | Existing emergency stop chain remains unchanged |
| Bounded control | Voltage and slew-rate limits always applied in Auto mode |
| Detectability | Fault states visible on supervision dashboard |

## 9. Future Evolution

### 9.1 Digital Twin Path

| Stage | Capability |
|---|---|
| Foundation | Real-time state + historical traces |
| Enhanced Twin | Simulation-assisted tuning and what-if analysis |
| Optimization | Data-driven control improvements and broader IT integration |

## 10. Appendices

### 10.1 Glossary

| Term | Definition |
|---|---|
| CT | Cycle Time |
| FO | Fabrication Order |
| VFD | Variable Frequency Drive |
| DAC | Digital-to-Analog Converter |
| MQTT | Publish/subscribe message protocol |

### 10.2 MQTT Topics

| Direction | Topic | Purpose |
|---|---|---|
| API → Edge | yazaki/line/{lineId}/ct | CT command payload |
| Edge → API | yazaki/line/{lineId}/speed | Speed/voltage feedback |

### 10.3 Key Parameters

| Parameter | Example | Purpose |
|---|---|---|
| MIN_VOLTAGE | 1.0 | Prevent stall region |
| MAX_VOLTAGE | 9.5 | Prevent overspeed region |
| FILTER_WINDOW_SIZE | 5 | CT smoothing |
| MQTT_TIMEOUT_SEC | 30 | Freshness threshold |

### 10.4 Validation Matrix

| Test Group | Coverage | Status |
|---|---|---|
| Unit | CT and message handling | Complete/Partial |
| Integration | MQTT + edge + dashboard | In Progress |
| Commissioning | DAC/NORDAC wiring and calibration | Pending |
| Pilot | Production validation under supervision | Planned |

---

Version: 3.0 (Full Remake)
Date: 2026-02-27
Status: Ready for review and PDF publication