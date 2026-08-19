# Adapter Template
The adapter template is a starting point for creating a new adapter. The adapter template can be installed in order to create your own adapters at will.

# Adapter Initial File Creation
## Using Local Template Installation
Ensure the .NET 10 SDK is installed on your machine.

Open Powershell and navigate to `Source\Template`. 

There will be a `.template.config/template.json` file there. Use the command `dotnet new install ./` to install the AdapterTemplate.

To ensure it has been installed, use `dotnet new list` and find the AdapterTemplate under Template Name

Now navigate to the directory in which you would like to create your adapter. Use the command `dotnet new AdapterTemplate --param:name "YourAdapterName"`. Note that `"YourAdapterName"` should be replaced with the real name of the adapter, such as "Modbus", "OpcUa", "DNP3",...

After success, the files will be created in that directory. You can open up your new solution under `YourAdapterName.sln` and the projects will load.