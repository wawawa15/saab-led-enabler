# saab-led-enabler
Saab 9-3 NG LED Enabler

A Windows utility for enabling LED bulbs on the Saab 9-3 NG while preventing the bulb warning message and LED flickering caused by the factory lighting monitoring system.

The application modifies the relevant REC and UEC configuration so that LED replacements can operate correctly without triggering the original bulb-failure monitoring.

For Saab 9-3 NG (2003–2011)

Features

* Enable LED bulbs without bulb-failure warnings
* Prevent LED flickering caused by PWM/bulb monitoring
* Modify the required REC/UEC configuration
* Restore the original configuration
* Simple Windows interface
* Automatic update support
* Supports multiple Saab diagnostic interfaces

Supported modules

The application communicates with the vehicle’s:

* REC — Rear Electrical Centre
* UEC — Underhood Electrical Centre

The required configuration is written directly to the vehicle modules.

Requirements

* Windows PC
* Saab 9-3 NG
* Compatible diagnostic interface
* Vehicle battery in good condition
* Ignition switched ON during programming

Diagnostic interfaces

Currently tested with:

* GM MDI2 + Tech2Win
* MDI2 / J2534-compatible interfaces

Additional interfaces may work but have not been fully tested.

Installation

1. Download the latest release from the Releases section.
2. Extract the application if necessary.
3. Run SaabLedEnabler.exe.
4. Connect your diagnostic interface.
5. Follow the instructions displayed by the application.

No additional .NET installation is required when using the self-contained release.

Usage

1. Connect the diagnostic interface to the Saab.
2. Start the application.
3. Select the required operation.
4. Follow the instructions shown on screen.
5. After programming is complete, cycle the ignition as instructed.

Important

Keep the vehicle battery voltage stable during programming.

Do not disconnect the diagnostic interface or switch off the ignition while a module is being programmed.

Restore original configuration

The application is designed to allow the original configuration to be restored.

If you return the vehicle to conventional incandescent bulbs, restore the original configuration to re-enable the factory bulb monitoring behavior.

Compatibility

This project is intended for the Saab 9-3 NG.

Different model years, body styles and lighting configurations may use different module configurations. Always verify compatibility before writing configuration data to a vehicle.

Disclaimer

This software is provided as-is, without warranty.

Use it at your own risk. The author is not responsible for damage to vehicle modules, diagnostic interfaces, wiring, or other components resulting from improper use.

Always maintain a stable vehicle battery voltage during programming.

Releases

Download the latest version from:

GitHub Releases

Changelog

v1.0.2

* Added Disable All function

v1.0.1

* Added automatic update functionality
* Updated application icon

v1.0.0

* Initial beta release

Contributing

If you test the application on a Saab 9-3 NG, feedback is welcome.

Please include:

* Model year
* Body style
* Module configuration
* Diagnostic interface used
* Operation performed
* Result
* Any error codes or unexpected behavior

Credits

Developed for the Saab 9-3 NG community.

If this project helped you, consider giving the repository a ⭐ on GitHub.
