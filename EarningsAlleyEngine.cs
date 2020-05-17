using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage;
using Newtonsoft.Json;
using System.Collections.Generic;
using TheMotleyFool.Transcripts;
using TheMotleyFool.Transcripts.Helper;
using Yahoo.Finance;

namespace EarningsAlley
{
    public class EarningsAlleyEngine
    {
        //Private resources here
        private EarningsAlleyLoginPackage LoginPackage;
        private CloudStorageAccount CSA;
        private CloudBlobClient CBC;
        private CloudBlobContainer ContainerGeneral;

        //Settings here
        private string RecentlyCompletedTranscriptUrlsBlockBlobName = "RecentlyCompletedTranscriptUrls";

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
            SentenceValuePair[] SVPs = null;
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
                int loc1 = trans.Title.IndexOf("(");
                int loc2 = trans.Title.IndexOf(")", loc1+1);
                string symbol = trans.Title.Substring(loc1 + 1, loc2 - loc1 - 1);
                Equity eq = Equity.Create(symbol);
                await eq.DownloadSummaryAsync();
                Tweet1Text = eq.Summary.Name + "(" + symbol.ToUpper().Trim() + ") held an earnings call on " + trans.CallDateTimeStamp.ToShortDateString() + ". Here are the highlights:";
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
                        CallParticipant speaker = trans.WhoSaid(SVPs[t].Sentence);

                        //Get the sentence
                        string sen = SVPs[t].Sentence;

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


        
    }
}