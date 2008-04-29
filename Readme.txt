================================================================================
WwwProxy 1.2.2.1
Copyright ©2008 Liam Kirton <liam@int3.ws>

29th April 2008
http://int3.ws/
================================================================================

Overview:
---------

WwwProxy is a C# library that provides all of the the networking functionality
required to implement an intercepting web application proxy (cf. Burp, Paros).

WwwProxy is licensed under the GNU Lesser General Public License. See the
included License.txt or http://www.gnu.org/licenses/lgpl.txt

Please email me (liam@int3.ws) if you would like more information and/or
a deeper technical explanation of the library.

Features:
---------

WwwProxy features the ability to capture, modify and transmit both HTTP and
HTTPS traffic. The library consumer is provided with a simple event-driven
interface.

WwwProxy supports the proxying of NTLM authentication. Also, an event
pre-handling plugin system is used to allow dynamic scripting language
modification of each request/response before it is passed to the library
consumer. This makes use of the Microsoft Dynamic Language Runtime, and
should ultimately allow scripts to be written in many different languages.
Currently only IronPython is supported, but this will improve once the DLR
interface stabilises.

It is possible to produce a custom event pre-handling plugin by creating a .NET
assembly which references WwwProxy.dll and implements WwwProxy.IPlugin. See the
implementation of WwwProxyScripting for details.

Usage:
------

See the included WwwProxyClient application for a basic example of WwwProxy
usage.

Using WwwProxyClient.exe:

> WwwProxyClient.exe
> WwwProxyClient.exe -localport 8081
> WwwProxyClient.exe -remote 10.0.0.1:8080 -remote-exceptions 10.0.0.*;*.intra.net
> WwwProxyClient.exe -certificate C:\Certificates\WwwProxy_1.cer -ntlm -plugins

Certificates:
-------------

WwwProxy requires a self-signed SSL certificate, "WwwProxy.cer", that is
correctly installed in the local machine certificate store.

-> Generating (Optional)

   To generate a new self-signed root certificate and WwwProxy certificate pair,
   run Makecert.bat (requires makecert.exe from Microsoft, part of the .NET
   Framework SDK). This also performs the necessary installation.

   Note that generation isn't necessary when existing certificates are imported.

-> Importing Existing

   To import an existing certificate pair (WwwProxyRoot.pfx and WwwProxy.pfx)
   into the local certificate store, run ImportPfx.vbs. This requires the
   Microsoft redistributable library Capicom.dll (included).

   !!! Capicom is a 32-bit redistributable .dll supplied by Microsoft. In order
   !!! to successfully import certificates on x64 platforms, the 32-bit
   !!! scripting host should be used, e.g.
   !!! > C:\Windows\SysWOW64\cscript.exe ImportPfx.vbs

-> Running

   WwwProxy.cer must exist in the current working directory for the application.

Scripting:
----------

The WwwProxyScripting plugin is controlled through Config\WwwProxyScripting.ini.
This allows specification of the language engine (currently .py is supported)
and the source file to load. The debug variable also enables script debug
logging, which will generate WwwProxyScripting_Debug.log containing error
messages.

Please note that WwwProxyScripting.dll must exist in the same directory as
WwwProxy.dll, and that IronPython.dll, IronPython.Modules.dll and
Microsoft.Scripting.dll must also exist in that directory.

A selection of example scripts are supplied in Scripts\IronPython.

================================================================================