# TickRecorder

Most trading applications only store the minute data and only the bid feed so it is difficult to get the high resolution tick data as well as the ask feed which allows for an accurate calculation of the spread.

This application records live bid and ask tick data from your forex broker using the cTrader API. The data is stored directly to your hardrive. This data can later be used for accurate backtesting of trading strategies

## Installation

Clone the repository.
Build in Visual Studio 2017

## Usage

- A cTrader API account needs to be created at connect.spotware.com
- Create a file in the applications local directory called config.txt
- The fille should have the format:

ClientId=ClientID

ClientSecret=ClientSecret

ApiHost=demo.ctraderapi.com

ApiPort=5035

- You then need to authorise a cTrader account (demo is fine) to use your cTrader application. Follow the directions at connect.spotware.com
- Once the account is authorised create a file in the application directory local/users/config.txt
- The file should have the format:

Token=Your token
  
RefreshToken=Refresh token

DataPath=Local path to store data

- This file will also get filled with all the available symbols for your account once a connection has been made
- You can delete the symbols from this file that you do not wish to record data for.
- Run the application and live tick data will be stored for each symbol, one day of tick data is stored in each file.


## License
[MIT](https://choosealicense.com/licenses/mit/)
