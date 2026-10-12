Saab 9-3 NG LED Enabler

Open-source bulb-check configuration tool for Saab 9-3 NG (2003–2011)

Developed by BCR Auto

1. Project Overview

Saab 9-3 NG LED Enabler is a Windows application designed to read and modify the bulb-check configuration stored in the vehicle’s electronic control modules.

When conventional halogen bulbs are replaced with LEDs, the vehicle may detect unexpected electrical characteristics, resulting in:

* LED flickering caused by periodic bulb-check pulses.
* Bulb failure warnings.
* Unwanted diagnostic pulses when the lights are switched off.

The application allows individual bulb-check functions to be enabled or disabled without replacing the vehicle’s original control modules or installing external load resistors.

The application currently supports configuration of two modules:

* UEC (Underhood Electrical Centre): Front exterior lighting.
* REC (Rear Electrical Centre): Rear exterior lighting.

It communicates with these modules through a compatible J2534 pass-through interface using CAN transport and KWP2000 diagnostic services.

2. Application Architecture

The application is developed in C# using .NET 8 and Windows Forms.

Its main components are:

Component	Responsibility
MainForm	User interface and configuration controls
J2534 transport	Communication with compatible pass-through interfaces
ISO-TP transport	CAN message segmentation and reassembly
GmlanDidService	Diagnostic identifier reading and writing
Configuration encoder	Converts checkbox states into configuration bytes
BackupManager	Stores original module configurations
Read-back verification	Confirms that written configurations were stored

General workflow

1. The user connects a compatible J2534 diagnostic interface to the vehicle.
2. The application establishes communication with the selected module.
3. It reads the module’s configuration data.
4. The received bytes are decoded into individual bulb-check settings.
5. The user enables or disables the desired settings.
6. The application modifies only the relevant bits.
7. The modified configuration is written back to the module.
8. The configuration is read again to verify the result.

The application preserves unrelated configuration bits to avoid modifying unknown module parameters.

3. CAN Communication

The application uses the following CAN identifiers:

Module	Tester → ECU	ECU → Tester	Configuration DID
UEC	0x24F	0x64F	0x4D
REC	0x249	0x649	0x45

These identifiers have been used successfully to read configuration data from the vehicle.

Diagnostic services

The configuration operations use KWP2000-style services.

Operation	Request SID	Positive response SID
Read configuration	0x1A	0x5A
Write configuration	0x3B	0x7B (expected)

The application handles CAN transport separately from the diagnostic payload.

Important: A J2534 message buffer may include a four-byte CAN identifier prefix. This prefix is not part of the eight-byte CAN data field.

4. UEC Configuration

The original UEC configuration observed on the test vehicle was:

8B F1 6B 6B

UEC byte layout

Byte	Observed value	Description
0	0x8B	Bulb-check timing parameter (interpretation not experimentally verified)
1	0xF1	Front lighting bulb-check flags
2	0x6B	Unmodified configuration data
3	0x6B	Unmodified configuration data

UEC bulb-check bit mapping

All supported UEC flags are stored in byte 1.

Lighting function	Bit	Hex mask	Enabled in 0xF1
Parking Lights	7	0x80	Yes
Front Fog Lights	6	0x40	Yes
Low Beams	5	0x20	Yes
High Beams	4	0x10	Yes
Front Turn Signals	0	0x01	Yes

The supported flag mask is:

0x80 | 0x40 | 0x20 | 0x10 | 0x01 = 0xF1

UEC configuration examples

Configuration	Byte 1	Complete configuration
Original, all supported checks enabled	F1	8B F1 6B 6B
Parking Lights disabled	71	8B 71 6B 6B
Front Fog Lights disabled	B1	8B B1 6B 6B
Low Beams disabled	D1	8B D1 6B 6B
High Beams disabled	E1	8B E1 6B 6B
Front Turn Signals disabled	F0	8B F0 6B 6B
All supported checks disabled	00	8B 00 6B 6B

These examples assume the original configuration is 8B F1 6B 6B.

UEC write example

Writing the original configuration:

CAN ID: 0x24F
06 3B 4D 8B F1 6B 6B 05

Frame interpretation:

Field	Value	Meaning
ISO-TP PCI	06	Six diagnostic payload bytes
Service	3B	Write configuration
Identifier	4D	UEC configuration
Data	8B F1 6B 6B	Four configuration bytes
Trailing byte	05	Outside the declared ISO-TP payload

To disable Parking Lights:

CAN ID: 0x24F
06 3B 4D 8B 71 6B 6B 05

Only byte 1 of the configuration changes.

The trailing 0x05 is not part of the six-byte diagnostic payload. Its exact role in the observed frame should not be assumed to be a checksum or configuration byte.

5. REC Configuration

The original REC configuration observed on the test vehicle was:

8B F8 FC 38 38 26

REC byte layout

Byte	Observed value	Description
0	0x8B	Bulb-check timing parameter (interpretation not experimentally verified)
1	0xF8	Rear lighting bulb-check flags
2	0xFC	Includes Rear Turn Signals flag
3	0x38	Unmodified configuration data
4	0x38	Unmodified configuration data
5	0x26	Unmodified configuration data

REC bulb-check bit mapping

Lighting function	Byte	Bit	Hex mask
Tail Lights	1	7	0x80
Rear Fog Lights	1	6	0x40
Stop Lights	1	5	0x20
Reverse Lights	1	4	0x10
License Plate Lights	1	3	0x08
Rear Turn Signals	2	3	0x08

The supported flags in byte 1 form the mask:

0x80 | 0x40 | 0x20 | 0x10 | 0x08 = 0xF8

Rear Turn Signals use byte 2 independently.

REC configuration examples

Configuration	Complete configuration
Original, all supported checks enabled	8B F8 FC 38 38 26
Tail Lights disabled	8B 78 FC 38 38 26
Rear Fog Lights disabled	8B B8 FC 38 38 26
Stop Lights disabled	8B D8 FC 38 38 26
Reverse Lights disabled	8B E8 FC 38 38 26
License Plate Lights disabled	8B F0 FC 38 38 26
Rear Turn Signals disabled	8B F8 F4 38 38 26
All supported checks disabled	8B 00 F4 38 38 26

Notice that disabling Rear Turn Signals changes byte 2 from FC to F4.

All other bits remain unchanged.

6. REC ISO-TP Multi-Frame Communication

Unlike the UEC configuration, the REC configuration cannot fit into a single ISO-TP CAN frame when written.

The diagnostic write payload contains eight bytes:

3B 45 8B F8 FC 38 38 26

Therefore, ISO-TP segmentation is required.

First Frame

CAN ID: 0x249
10 08 3B 45 8B F8 FC 38

Field	Value	Meaning
ISO-TP header	10 08	First Frame, total payload length 8
Service	3B	Write configuration
Identifier	45	REC configuration
Configuration	8B F8 FC 38	First four configuration bytes

Flow Control

The application waits for the receiving module’s ISO-TP Flow Control message before sending the remaining data.

The Flow Control message determines whether transmission can continue and may specify timing requirements.

Consecutive Frame

CAN ID: 0x249
21 38 26 00 00 00 00 00

Field	Value	Meaning
ISO-TP PCI	21	Consecutive Frame, sequence number 1
Remaining configuration	38 26	Last two bytes
Padding	00 00 00 00 00	CAN frame padding

Example: Disable Rear Turn Signals

Original configuration:

8B F8 FC 38 38 26

Modified configuration:

8B F8 F4 38 38 26

Transmitted First Frame:

CAN ID: 0x249
10 08 3B 45 8B F8 F4 38

Transmitted Consecutive Frame:

CAN ID: 0x249
21 38 26 00 00 00 00 00

The module’s other configuration values are preserved.

7. Reading and Decoding Configuration Data

A successful read operation returns a positive diagnostic response containing the configuration identifier and configuration bytes.

UEC read response

Observed J2534 buffer:

00 00 06 4F 06 5A 4D 8B F1 6B 6B 05

Interpretation:

CAN ID prefix: 00 00 06 4F
CAN payload:   06 5A 4D 8B F1 6B 6B 05

After ISO-TP processing:

5A 4D 8B F1 6B 6B

After removing the positive-response service and identifier:

8B F1 6B 6B

This four-byte array is passed to the UEC configuration decoder.

REC read response

Observed First Frame:

CAN ID: 0x649
10 08 5A 45 8B F8 FC 38

Observed Consecutive Frame:

CAN ID: 0x649
21 38 26 00 00 00 00 00

After ISO-TP reassembly:

5A 45 8B F8 FC 38 38 26

After removing the positive-response service and identifier:

8B F8 FC 38 38 26

The application decodes the relevant bits to populate the interface controls.

Example: decoding Parking Lights

bool parkingLightsEnabled =
    (uecConfig[1] & 0x80) != 0;

Example: decoding Rear Turn Signals

bool rearTurnSignalsEnabled =
    (recConfig[2] & 0x08) != 0;

The array indexes above refer to the extracted configuration bytes, not the raw CAN frame.

8. Safe Configuration Encoding

One of the application’s most important design principles is preserving unknown configuration bits.

The application must not overwrite an entire configuration byte with a fixed value unless all bits in that byte are understood.

Instead, it updates only the supported flags.

UEC encoder

data[0] = pwm;
data[1] = (byte)(
    (data[1] & ~0xF1) | uecMask
);

REC encoder

data[0] = pwm;
data[1] = (byte)(
    (data[1] & ~0xF8) | recMask1
);
data[2] = (byte)(
    (data[2] & ~0x08) | recMask2
);

Here, pwm is the historical variable name used by the application for the first configuration byte. It should not be interpreted as proof that this byte controls PWM.

The encoder starts with the configuration read from the vehicle and applies only the requested changes.

This approach protects unrelated configuration values and improves compatibility with different module configurations.

9. Understanding the First Byte: 0x8B

Both modules on the test vehicle reported 0x8B as their first configuration byte.

0x8B = 139 decimal

The open-source project roffe/hcd documents this byte as a periodic bulb-check interval expressed in units of approximately 14.5 milliseconds.

Using that interpretation:

139 × 14.5 ms = 2015.5 ms

This corresponds to approximately 2.02 seconds between checks.

That project also documents 0x00 as a value that disables periodic checking.

Experimental status: The timing interpretation and the effect of setting this byte to zero have not been independently verified on our test vehicle.

The application has primarily been tested by modifying the individual bulb-check flags while preserving 0x8B.

Until additional testing is completed, developers should avoid treating this byte as a confirmed PWM duty-cycle setting or modifying it without a recoverable backup.

10. Configuration Backup and Verification

Before modifying a module, the application can save its original configuration.

This allows the user to restore the previous values if necessary.

After writing a modified configuration, the application performs a read-back operation.

The newly received configuration is compared with the requested values.

The operation should only be considered verified when the relevant bytes match.

This is especially important because a successful transport-level transmission does not necessarily mean that the module permanently accepted the configuration.

11. Supported Lighting Channels

The application currently provides individual controls for 11 bulb-check functions.

Module	Lighting function
UEC	Parking Lights
UEC	Front Fog Lights
UEC	Low Beams
UEC	High Beams
UEC	Front Turn Signals
REC	Tail Lights
REC	Rear Fog Lights
REC	Stop Lights
REC	Reverse Lights
REC	License Plate Lights
REC	Rear Turn Signals

Disabling a bulb-check flag is intended to disable the corresponding diagnostic check, not the normal lighting function itself.

However, disabling a check can also suppress detection of a genuinely failed bulb on that circuit.

12. Hardware and Software Requirements

* Windows operating system.
* Compatible J2534 pass-through diagnostic interface.
* .NET 8 runtime, unless using the self-contained build.
* Supported Saab 9-3 NG vehicle and compatible UEC/REC modules.

Tested hardware

Development and vehicle testing were performed using a GM MDI2 interface.

Other J2534 interfaces may work, but compatibility should be tested individually.

Tech2 support

Physical Tech2 communication through RS232 is not currently a validated feature of this application.

Tech2Win virtual serial ports should not be assumed to provide a compatible diagnostic connection.

13. Vehicle Testing and Validation

The following observations were obtained during testing on a 2004 Saab 9-3 NG.

Confirmed

* Communication with UEC through CAN ID 0x24F.
* Communication with REC through CAN ID 0x249.
* Reading the original UEC configuration 8B F1 6B 6B.
* Reading the original REC configuration 8B F8 FC 38 38 26.
* Successful configuration writing and read-back verification.
* Disabling the Parking Lights check through UEC.
* Disabling the Rear Turn Signals check through REC.
* Elimination of the observed LED flickering on those tested circuits.
* Normal operation of those lights after disabling their checks.

Not yet fully validated

* Every individual lighting channel on different vehicle configurations.
* Changing the first configuration byte (0x8B).
* Compatibility with all model years and module variants.
* Physical Tech2 RS232 communication.
* All available J2534 interfaces.

The project is therefore considered experimentally validated for the tested configuration, rather than universally validated for every Saab 9-3 NG variant.

14. Safety and Responsible Use

This application modifies configuration data in vehicle control modules.

Incorrect configuration values, interrupted communication, or incompatible modules may cause unexpected electrical or diagnostic behavior.

Before writing configuration data:

1. Maintain a stable vehicle battery voltage.
2. Save the original configuration.
3. Verify that the correct module is selected.
4. Avoid interrupting the communication session.
5. Read back the configuration after writing.
6. Confirm that all affected lights operate correctly.

Disabling bulb checks may also disable bulb failure monitoring. Users are responsible for ensuring that their vehicle’s exterior lighting remains functional and compliant with applicable regulations.

15. Open-Source Development

The purpose of publishing this project is to help the Saab community understand and maintain the electronic systems of the Saab 9-3 NG.

Contributions are welcome in areas such as:

* Additional J2534 interface compatibility.
* Physical Tech2 communication support.
* Further reverse engineering of configuration bytes.
* Improved diagnostic logging.
* Compatibility testing across model years.
* User interface improvements.
* Bug fixes and documentation.

Developers are encouraged to document all new findings and distinguish experimentally confirmed behavior from assumptions.


This project provides useful information about Saab 9-3 NG bulb-check configuration, including CAN identifiers, bit mappings, and the possible interpretation of the timing byte.


⸻

Saab 9-3 NG LED Enabler

Developed by BCR Auto

Your new way to see.
