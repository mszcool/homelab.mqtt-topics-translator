# Infrastructure configuration steps

* First, create a user called mszworker using `sudo adduser mszworker`
* Copy the .NET files to /home/mszworker/mqtttrans/*
* Make sure to copy the proper configuration files in the same folder:
  * appsettings.json with proper settings
  * mqttTranslatorConfig.json with the topic translation files
* Install .NET Core 8 on the target machine under /usr/bin/dotnet8 using the install script `./dotnet-install.sh -i /usr/bin/dotnet8`
* Then, copy the service file from this folder (mszmqttworker.service) into /etc/systemd/system folder.
* Then reload the systemd daemon: `sudo systemctl daemon-reload`
* Enable the service: `sudo systemctl enable mszmqttworker.service`
* Start the service: `sudo systemctl start mszmqttworker.service`
* Check the status of the service: `sudo systemctl status mszmqttworker.service`
