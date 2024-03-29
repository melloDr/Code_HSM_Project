﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Aws4RequestSigner
{
    public class AwsV4SignatureCalculator
    {
        public const string Iso8601DateTimeFormat = "yyyyMMddTHHmmssZ";
        public const string Iso8601DateFormat = "yyyyMMdd";

        private readonly string _awsSecretKey = "eHi3x+c4nQZYuVQRZV3d1WcB46aCzMQhNR4KcSpY";
        private readonly string _service = "sign";
        private readonly string _region = "hn1";

        /// <param name="service">AWS service name, e.g. "sqs" or "ec2, IS: sign"</param>
        /// <param name="region">AWS region to send requests to, IS: hn1</param>
        /// Truyen
        public AwsV4SignatureCalculator(string awsSecretKey, string service, string region = null)
        {
            _awsSecretKey = awsSecretKey;
            _service = service;
            _region = region ?? "us-east-1";
        }

        /// <summary>
        /// Calculates request signature string using Signature Version 4.
        /// link: http://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
        /// </summary>
        /// <param name="request">Request</param> //Maybe URL
        /// <param name="signedHeaders">Canonical headers that are a part of a signing process</param>
        /// <param name="requestDate">Date and time when request takes place</param>
        /// <returns>Signature</returns>
        public string CalculateSignature(HttpRequestMessage request, string[] signedHeaders, DateTime requestDate)
        {
            signedHeaders = signedHeaders.Select(x => x.Trim().ToLowerInvariant()).OrderBy(x => x).ToArray();

            var canonicalRequest = GetCanonicalRequest(request, signedHeaders);
            var stringToSign = GetStringToSign(requestDate, canonicalRequest);
            return GetSignature(requestDate, stringToSign);
        }

        // link: http://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
        private static string GetCanonicalRequest(HttpRequestMessage request, string[] signedHeaders)
        {
            var canonicalRequest = new StringBuilder();
            canonicalRequest.AppendFormat("{0}\n", request.Method.Method);
            canonicalRequest.AppendFormat("{0}\n", request.RequestUri.AbsolutePath);
            //canonicalRequest.AppendFormat("{0}\n", GetCanonicalQueryParameters(request.RequestUri.ParseQueryString()));
            canonicalRequest.AppendFormat("{0}\n", GetCanonicalQueryParams(request));
            var querystring = HttpUtility.ParseQueryString(request.RequestUri.Query);
            

            canonicalRequest.AppendFormat("{0}\n", GetCanonicalHeaders(request, signedHeaders));
            canonicalRequest.AppendFormat("{0}\n", String.Join(";", signedHeaders));
            canonicalRequest.Append(GetPayloadHash(request));
            return canonicalRequest.ToString();
        }
        private static string GetCanonicalQueryParameters(NameValueCollection queryParameters)
        {
            StringBuilder canonicalQueryParameters = new StringBuilder();
            foreach (string key in queryParameters)
            {
                canonicalQueryParameters.AppendFormat("{0}={1}&", Utils.UrlEncode(key),
                                                      Utils.UrlEncode(queryParameters[key]));
            }

            // remove trailing '&'
            if (canonicalQueryParameters.Length > 0)
                canonicalQueryParameters.Remove(canonicalQueryParameters.Length - 1, 1);

            return canonicalQueryParameters.ToString();
        }

        private static string GetCanonicalQueryParams(HttpRequestMessage request)
        {
            var values = new SortedDictionary<string, string>();

            var querystring = HttpUtility.ParseQueryString(request.RequestUri.Query);
            foreach (var key in querystring.AllKeys)
            {
                if (key == null)//Handles keys without values
                {
                    values.Add(Uri.EscapeDataString(querystring[key]), $"{Uri.EscapeDataString(querystring[key])}=");
                }
                else
                {
                    // Escape to upper case. Required.
                    values.Add(Uri.EscapeDataString(key), $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(querystring[key])}");
                }
            }
            // Put in order - this is important.
            var queryParams = values.Select(a => a.Value);
            return string.Join("&", queryParams);
        }

        private static string GetCanonicalHeaders(HttpRequestMessage request, IEnumerable<string> signedHeaders)
        {
            var headers = request.Headers.ToDictionary(x => x.Key.Trim().ToLowerInvariant(),
                                                       x => String.Join(" ", x.Value).Trim());

            if (request.Content != null)
            {
                var contentHeaders = request.Content.Headers.ToDictionary(x => x.Key.Trim().ToLowerInvariant(),
                                                                          x => String.Join(" ", x.Value).Trim());
                foreach (var contentHeader in contentHeaders)
                {
                    headers.Add(contentHeader.Key, contentHeader.Value);
                }
            }

            var sortedHeaders = new SortedDictionary<string, string>(headers);

            StringBuilder canonicalHeaders = new StringBuilder();
            foreach (var header in sortedHeaders.Where(header => signedHeaders.Contains(header.Key)))
            {
                canonicalHeaders.AppendFormat("{0}:{1}\n", header.Key, header.Value);
            }
            return canonicalHeaders.ToString();
        }



        private static string GetPayloadHash(HttpRequestMessage request)
        {
            var payload = request.Content != null ? request.Content.ReadAsStringAsync().Result : "";
            return Utils.ToHex(Utils.Hash(payload));
        }

        // http://docs.aws.amazon.com/general/latest/gr/sigv4-create-string-to-sign.html
        private string GetStringToSign(DateTime requestDate, string canonicalRequest)
        {
            var dateStamp = requestDate.ToString(Iso8601DateFormat, CultureInfo.InvariantCulture);
            var scope = string.Format("{0}/{1}/{2}/{3}", dateStamp, _region, _service, "aws4_request");

            var stringToSign = new StringBuilder();
            stringToSign.AppendFormat("AWS4-HMAC-SHA256\n{0}\n{1}\n",
                                      requestDate.ToString(Iso8601DateTimeFormat, CultureInfo.InvariantCulture),
                                      scope);
            stringToSign.Append(Utils.ToHex(Utils.Hash(canonicalRequest)));
            return stringToSign.ToString();
        }

        // http://docs.aws.amazon.com/general/latest/gr/sigv4-calculate-signature.html
        private string GetSignature(DateTime requestDate, string stringToSign)
        {
            var kSigning = GetSigningKey(requestDate);
            return Utils.ToHex(Utils.GetKeyedHash(kSigning, stringToSign));
        }

        private byte[] GetSigningKey(DateTime requestDate)
        {
            var dateStamp = requestDate.ToString(Iso8601DateFormat, CultureInfo.InvariantCulture);
            var kDate = Utils.GetKeyedHash("AWS4" + _awsSecretKey, dateStamp);
            var kRegion = Utils.GetKeyedHash(kDate, _region);
            var kService = Utils.GetKeyedHash(kRegion, _service);
            return Utils.GetKeyedHash(kService, "aws4_request");
        }

        private static class Utils
        {
            private const string ValidUrlCharacters =
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

            public static string UrlEncode(string data)
            {
                StringBuilder encoded = new StringBuilder();
                foreach (char symbol in Encoding.UTF8.GetBytes(data))
                {
                    if (ValidUrlCharacters.IndexOf(symbol) != -1)
                    {
                        encoded.Append(symbol);
                    }
                    else
                    {
                        encoded.Append("%").Append(string.Format(CultureInfo.InvariantCulture, "{0:X2}", (int)symbol));
                    }
                }
                return encoded.ToString();
            }

            public static byte[] Hash(string value)
            {
                return new SHA256CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            public static byte[] GetKeyedHash(string key, string value)
            {
                return GetKeyedHash(Encoding.UTF8.GetBytes(key), value);
            }

            public static byte[] GetKeyedHash(byte[] key, string value)
            {
                KeyedHashAlgorithm mac = new HMACSHA256(key);
                mac.Initialize();
                return mac.ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            public static string ToHex(byte[] data)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }
    }
}
