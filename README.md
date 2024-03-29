# Habbo2020-Desktop

_Work in progress_

This repository is a continuation of my [research in 2020](https://github.com/UnfamiliarLegacy/Habbo2020).  
The main intent is to help out [Tanji](https://github.com/ArachisH/Tanji) to support the Unity client.

## Warning

**Never** connect to the Habbo server with an invalid client certificate.  
If you do this **you will get banned for 100 years**.  

This project has certificate validation built in to prevent this mistake.  
**Use at your own risk.**

## Usage

1. Refresh the self-signed SSL certificates in `src/MitmServerNet/Certificates/Self`.   
`openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -sha256 -days 3650 -nodes -subj "/C=XX/ST=StateName/L=CityName/O=CompanyName/OU=CompanySectionName/CN=CommonNameOrHostname"`
2. Enter Habbo certificate password in `src/MitmServerNet/appsettings.json`.
