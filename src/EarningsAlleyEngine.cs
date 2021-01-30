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

        public async Task<string[]> PrepareTweetsForUpcomingEarningsCallsAsync(DateTime day)
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
            BatchStockDataProvider bsdp = new BatchStockDataProvider();
            EquitySummaryData[] data = await bsdp.GetBatchEquitySummaryData(stocks);


            //Rank by market cap
            List<EquitySummaryData> DataAsList = data.ToList();
            List<EquitySummaryData> Filter1 = new List<EquitySummaryData>();
            do
            {
                EquitySummaryData winner = DataAsList[0];
                foreach (EquitySummaryData esd in DataAsList)
                {
                    if (esd.MarketCap > winner.MarketCap)
                    {
                        winner = esd;
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
            foreach (EquitySummaryData esd in Filter1)
            {
                string MyPiece = t.ToString() + ". " + "$" + esd.StockSymbol.ToUpper().Trim() + " " + esd.Name;

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
        public async Task<StatementOfChangesInBeneficialOwnership[]> ObserveNewForm4sAsync()
        {
            //Get the last observed filing URL
            string LastObservedFilingUrl = await DownloadLatestObservedForm4FilingUrlAsync();

            //Search!
            EdgarLatestFilingsSearch elfs = await EdgarLatestFilingsSearch.SearchAsync("4", EdgarSearchOwnershipFilter.only, EdgarSearchResultsPerPage.Entries40);
            
            //Get a list of new filings
            List<EdgarSearchResult> NewlyObservedFilings = new List<EdgarSearchResult>();
            if (LastObservedFilingUrl != null)
            {
                foreach (EdgarSearchResult esr in elfs.Results)
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
                foreach (EdgarSearchResult esr in elfs.Results)
                {
                    NewlyObservedFilings.Add(esr);
                }
            }

            //Get a list of statmenet of changes in beneficial ownership for each of them
            List<StatementOfChangesInBeneficialOwnership> ToReturn = new List<StatementOfChangesInBeneficialOwnership>();
            foreach (EdgarSearchResult esr in NewlyObservedFilings)
            {
                FilingDocument[] docs = await esr.GetDocumentFormatFilesAsync();
                foreach (FilingDocument fd in docs)
                {
                    if (fd.DocumentName.ToLower().Contains(".xml") && fd.DocumentType == "4")
                    {
                        try
                        {
                            StatementOfChangesInBeneficialOwnership form4 = await StatementOfChangesInBeneficialOwnership.ParseXmlFromWebUrlAsync(fd.Url);
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

        #endregion

    }
}