using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage;
using Newtonsoft.Json;
using System.Collections.Generic;
using TheMotleyFool.Transcripts;
using TheMotleyFool.Transcripts.Helper;
using Yahoo.Finance;
using TimHanewich.Investing;
using System.Linq;
using System.Globalization;
using TimHanewichToolkit.TextAnalysis;
using EarningsAlley;
using SecuritiesExchangeCommission.Edgar;
using Aletheia.Service;
using Aletheia.Service.StockData;

namespace EarningsAlley
{
    public class EarningsAlleyEngine
    {
        //ENVIRONMENT VARIABLES HERE
        private static string RecentlyCompletedTranscriptUrlsBlockBlobName = "RecentlyCompletedTranscriptUrls";
        private static string RecentlyObservedForm4FilingsFileName = "RecentlyObservedForm4Filings";
        
        //Private resources here
        private EarningsAlleyLoginPackage LoginPackage;
        private CloudStorageAccount CSA;
        private CloudBlobClient CBC;
        private CloudBlobContainer ContainerGeneral;        

        public static EarningsAlleyEngine Create(EarningsAlleyLoginPackage login_package)
        {
            //Error checking
            if (login_package.AzureStorageConnectionString == null)
            {
                throw new Exception("Azure storage sonnection string was null.");
            }
            else
            {
                if (login_package.AzureStorageConnectionString == "")
                {
                    throw new Exception("Azure storage sonnection string was blank.");
                }
            }

            EarningsAlleyEngine ToReturn = new EarningsAlleyEngine();
            ToReturn.LoginPackage = login_package;

            //Connect
            try
            {
                CloudStorageAccount.TryParse(login_package.AzureStorageConnectionString, out ToReturn.CSA);
            }
            catch
            {
                throw new Exception("Fatal error while connecting to Azure with the supplied connection string.");
            }

            //CBC
            ToReturn.CBC = ToReturn.CSA.CreateCloudBlobClient();

            //Get container
            CloudBlobContainer cont = ToReturn.CBC.GetContainerReference("general");
            cont.CreateIfNotExists();
            ToReturn.ContainerGeneral = cont;
            
            return ToReturn;
        }
    
        #region "Transcript summary tweeting"

        public List<string> DownloadRecentlyCompletedTranscriptUrls()
        {
            CloudBlockBlob blob = ContainerGeneral.GetBlockBlobReference(RecentlyCompletedTranscriptUrlsBlockBlobName);
            if (blob.Exists())
            {
                string content = blob.DownloadText();
                List<string> lst = JsonConvert.DeserializeObject<List<string>>(content);
                return lst;
            }
            else
            {
                List<string> strs = new List<string>();
                return strs;
            }
        }
    
        public void UploadRecentlyCompletedTranscriptUrls(List<string> to_upload)
        {
            string json = JsonConvert.SerializeObject(to_upload);
            CloudBlockBlob blob = ContainerGeneral.GetBlockBlobReference(RecentlyCompletedTranscriptUrlsBlockBlobName);
            blob.UploadText(json);
        }
    
        public async Task<string[]> PrepareTweetsFromTranscriptAsync(Transcript trans, int include_highlight_count)
        {
            

            //Sentence Value Pairs
            TextValuePair[] SVPs = null;
            try
            {
                SVPs = trans.RankSentences();
            }
            catch (Exception e)
            {
                throw new Exception("Fatal error while ranking transcript sentences. Internal error message: " + e.Message);
            }

            List<string> ToTweet = new List<string>();

            #region "First Tweet (Intro)"

            string Tweet1Text = "Earnings Call Highlights"; //The value here should be replaced anyway.

            //Get company symbol
            if (trans.Title.Contains("(") && trans.Title.Contains(")"))
            {
                int loc1 = trans.Title.LastIndexOf("(");
                int loc2 = trans.Title.IndexOf(")", loc1+1);
                string symbol = trans.Title.Substring(loc1 + 1, loc2 - loc1 - 1);
                Equity eq = Equity.Create(symbol);
                await eq.DownloadSummaryAsync();
                Tweet1Text = eq.Summary.Name + " $" + symbol.ToUpper().Trim() + " held an earnings call on " + trans.CallDateTimeStamp.ToShortDateString() + ". Here are the highlights:";
            }
            else
            {
                Tweet1Text = trans.Title.Replace(" Transcript","") + " highlights: ";
            }

            ToTweet.Add(Tweet1Text);

            #endregion
            
            #region "Highlights"

            int t = 0;

            for (t = 0; t < include_highlight_count; t++)
            {
                if (SVPs.Length >= (t+1))
                {

                    try
                    {

                        //Find the speaker
                        CallParticipant speaker = trans.WhoSaid(SVPs[t].Text);

                        //Get the sentence
                        string sen = SVPs[t].Text;

                        //write the tweet
                        string ThisTweet = speaker.Name + ": \"" + sen + "\"";

                        //Trim it down (if it goes past 280 characters)
                        if (ThisTweet.Length > 280)
                        {
                            ThisTweet = ThisTweet.Substring(0, 276);
                            ThisTweet = ThisTweet + "...\"";
                        }

                        //Add it
                        ToTweet.Add(ThisTweet);

                    }
                    catch
                    {

                    }
                    


                }
            }

            #endregion


            return ToTweet.ToArray();
        }

        public async Task<string[]> PrepareTweetsForUpcomingEarningsCallsAsync(DateTime day, Guid aletheia_api_key)
        {
            List<string> ToReturn = new List<string>();
           
            //Get stocks
            EarningsCalendarProvider ecp = new EarningsCalendarProvider();
            string[] stocks = await ecp.GetCompaniesReportingEarningsAsync(day);
            if (stocks.Length == 0)
            {
                ToReturn.Add("No earnings calls planned for " + day.ToShortDateString() + "! Until next time.");
                return ToReturn.ToArray();
            }
            

            //Get Equity summary data
            AletheiaService service = new AletheiaService(aletheia_api_key);
            StockData[] AllData = await service.GetMultipleStockDataAsync(stocks, true, false);
            
            //Rank by market cap
            List<StockData> DataAsList = AllData.ToList();
            List<StockData> Filter1 = new List<StockData>();
            do
            {
                StockData winner = DataAsList[0];
                foreach (StockData esd in DataAsList)
                {
                    if (esd.SummaryData != null)
                    {
                        if (esd.SummaryData.MarketCap > winner.SummaryData.MarketCap)
                        {
                            winner = esd;
                        }
                    }
                }
                Filter1.Add(winner);
                DataAsList.Remove(winner);
            } while (DataAsList.Count != 0);


            int t = 1;
            string CurrentTweet = "";

            //First tweet
            DateTimeFormatInfo dtfo = new DateTimeFormatInfo();
            string monthName = dtfo.GetMonthName(day.Month);
            CurrentTweet = "Upcoming earnings calls on " + day.DayOfWeek.ToString() + ", " + monthName + " " + day.Day.ToString() + " " + day.Year.ToString() + ":";
            
            //Add all
            foreach (StockData esd in Filter1)
            {
                string MyPiece = t.ToString() + ". " + "$" + esd.SummaryData.StockSymbol.ToUpper().Trim() + " " + esd.SummaryData.Name;

                //Propose what it would be
                string proposal = CurrentTweet + "\n" + MyPiece;

                if (proposal.Length <=280) //If it fits, keep it in the current tweet
                {
                    CurrentTweet = proposal;
                }
                else //If it doesn't fit in the current tweet, commit that tweet to the Tweet list and start another
                {
                    ToReturn.Add(CurrentTweet);
                    CurrentTweet = MyPiece;
                }

                t = t + 1;
            }

            //Add the current tweet that is being worked on to the list
            ToReturn.Add(CurrentTweet);


            return ToReturn.ToArray();

        }
        
        #endregion
        
        #region "New Form 4 alert tweeting"

        public async Task<string> DownloadLatestObservedForm4FilingUrlAsync()
        {
            //If the container general does not exist, obviously neither does the file so return blank.
            if (ContainerGeneral.Exists() == false)
            {
                return null;
            }

            //Get the blob
            CloudBlockBlob blb = ContainerGeneral.GetBlockBlobReference(RecentlyObservedForm4FilingsFileName);
            
            //If the blob does not exist return nothing
            if (blb.Exists() == false)
            {
                return null;
            }

            string content = await blb.DownloadTextAsync();
            return content;
        }

        public async Task UploadLatestObservedForm4FilingUrlAsync(string url)
        {
            //If the container does not exit, make it
            if (ContainerGeneral == null || ContainerGeneral.Exists() == false)
            {
                await ContainerGeneral.CreateIfNotExistsAsync();
            }

            //Get the blob reference
            CloudBlockBlob blb = ContainerGeneral.GetBlockBlobReference(RecentlyObservedForm4FilingsFileName);
            await blb.UploadTextAsync(url);
        }

        /// <summary>
        /// This will scan the newly filed Form 4's and return the ones that are new (have not been seen yet). Thus, this will mark the new ones as observed.
        /// </summary>
        public async Task<StatementOfBeneficialOwnership[]> ObserveNewForm4sAsync()
        {
            //Get the last observed filing URL
            string LastObservedFilingUrl = await DownloadLatestObservedForm4FilingUrlAsync();

            //Search!
            EdgarLatestFilingsSearch elfs = await EdgarLatestFilingsSearch.SearchAsync("4", EdgarSearchOwnershipFilter.only, EdgarSearchResultsPerPage.Entries40);
            
            //Get a list of new filings
            List<EdgarLatestFilingResult> NewlyObservedFilings = new List<EdgarLatestFilingResult>();
            if (LastObservedFilingUrl != null)
            {
                foreach (EdgarLatestFilingResult esr in elfs.Results)
                {
                    if (LastObservedFilingUrl == esr.DocumentsUrl)
                    {
                        break;
                    }
                    else
                    {
                        NewlyObservedFilings.Add(esr);
                    }
                }   
            }
            else //If there isn't a latest received filings url in azure, just add all of them
            {
                foreach (EdgarLatestFilingResult esr in elfs.Results)
                {
                    NewlyObservedFilings.Add(esr);
                }
            }

            //Get a list of statmenet of changes in beneficial ownership for each of them
            List<StatementOfBeneficialOwnership> ToReturn = new List<StatementOfBeneficialOwnership>();
            foreach (EdgarLatestFilingResult esr in NewlyObservedFilings)
            {
                EdgarSearchResult esrt = new EdgarSearchResult();
                esrt.DocumentsUrl = esr.DocumentsUrl; //Have to plug it into here because this is the only class has has the GetDocumentFormatFilesAsync method
                FilingDocument[] docs = await esrt.GetDocumentFormatFilesAsync();
                foreach (FilingDocument fd in docs)
                {
                    if (fd.DocumentName.ToLower().Contains(".xml") && fd.DocumentType == "4")
                    {
                        try
                        {
                            StatementOfBeneficialOwnership form4 = await StatementOfBeneficialOwnership.ParseXmlFromWebUrlAsync(fd.Url);
                            ToReturn.Add(form4);
                        }
                        catch
                        {

                        }   
                    }
                }
            }

            //Log the most recent seen form 4 (it would just be the first one in the list of results)
            await UploadLatestObservedForm4FilingUrlAsync(elfs.Results[0].DocumentsUrl);

            //Return
            return ToReturn.ToArray();
        }

        public async Task<string> PrepareNewForm4TweetAsync(StatementOfBeneficialOwnership form4)
        {
            string ToReturn = null;

            foreach (NonDerivativeTransaction ndt in form4.NonDerivativeTransactions)
            {
                if (ndt.AcquiredOrDisposed == AcquiredDisposed.Acquired) //They acquired
                {
                    if (ndt.TransactionCode != null) //It is indeed a transaction, not just a holding report
                    {
                        if (ndt.TransactionCode == TransactionType.OpenMarketOrPrivatePurchase) //Open market purchase
                        {
                            //Get the equity cost
                            Equity e = Equity.Create(form4.IssuerTradingSymbol);
                            await e.DownloadSummaryAsync();

                            //Get the name to use
                            string TraderNameToUse = form4.OwnerName;
                            try
                            {
                                TraderNameToUse = Aletheia.AletheiaToolkit.NormalizeAndRearrangeForm4Name(form4.OwnerName);
                            }
                            catch
                            {

                            }

                            //Start
                            ToReturn = "*INSIDER BUY ALERT*" + Environment.NewLine;
                            ToReturn = ToReturn + form4.OwnerName;

                            //Is there an officer title? If so, loop it in
                            if (form4.OwnerOfficerTitle != null && form4.OwnerOfficerTitle != "")
                            {
                                ToReturn = ToReturn + ", " + form4.OwnerOfficerTitle + ", ";
                            }
                            else
                            {
                                ToReturn = ToReturn + " ";
                            }

                            //Continue
                            ToReturn = ToReturn + "purchased " + ndt.TransactionQuantity.Value.ToString("#,##0") + " shares of $" + form4.IssuerTradingSymbol.Trim().ToUpper();

                            //Was a transaction price supplied?
                            if (ndt.TransactionPricePerSecurity.HasValue)
                            {
                                ToReturn = ToReturn + " at $" + ndt.TransactionPricePerSecurity.Value.ToString("#,##0.00");
                            }

                            //Add a period
                            ToReturn = ToReturn + "." + Environment.NewLine;

                            //How much they own following transaction
                            float worth = e.Summary.Price * ndt.SecuritiesOwnedFollowingTransaction;
                            ToReturn = ToReturn + form4.OwnerName + " now owns " + ndt.SecuritiesOwnedFollowingTransaction.ToString("#,##0") + " shares worth $" + worth.ToString("#,##0") + " of " + form4.IssuerName + " stock.";
                        }
                    }     
                }
            }
        
            //Throw an error if ToReturn is still null (the above process did not satisfy anything)
            if (ToReturn == null)
            {
                throw new Exception("The Form 4 type is not supported for tweeting.");
            }

            return ToReturn;
        }

        #endregion

    }
}