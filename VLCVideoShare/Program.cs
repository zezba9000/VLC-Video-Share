#define ALLOW_DEBUG_PATH

using Reign;
using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// ============================
// Allow port on different OSes
// ============================
// Windows: netsh http add urlacl url=http://+:8085/ user=Everyone
//	-	Windows list: netsh http show urlacl
//	-	Windows remove: netsh http delete urlacl url=http://+:8080/

// macOS: (Nothing needed for port 8085)
// Linux: sudo ufw allow 8085

namespace VLCVideoShare
{
	enum RequestType
	{
		Normal,
		RequestFiles,
		DownloadFile,
		PlayVideoFile
	}

    static class Program
    {
        private static Thread httpThread;
		private static bool httpThreadAlive;

		private static HttpListener httpListener;
		private static List<string> sharePaths;

		private static OpenedNATDevice natDevice;

        static void Main(string[] args)
        {
			// get paths
			sharePaths = new List<string>();
			#if DEBUG && ALLOW_DEBUG_PATH
			string path = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\", "TestVideos").Replace('\\', Path.DirectorySeparatorChar);
			path = Path.GetFullPath(path);
			sharePaths.Add(path);
			#else
			if (args != null && args.Length != 0)
			{
				foreach (var arg in args)
				{
					sharePaths.Add(arg);
				}
			}
			#endif

			// start http thread
			Console.WriteLine("Type 'q' to exit");
			httpThreadAlive = true;
            httpThread = new Thread(HttpThread);
            httpThread.IsBackground = false;// let http thread dispose correctly
            httpThread.Start();

			// handle events
			while (true)
			{
				string value = Console.ReadLine();
				if (value == "q") break;
				else Console.WriteLine("Invalid command");
			}

			// shutdown
			httpThreadAlive = false;
			if (httpListener != null && httpListener.IsListening)
			{
				try
				{
					httpListener.Close();
				}
				catch { }
			}

			//NATUtils.ClosePort(natDevice);
			//Console.WriteLine("NAT closed");
        }

        private static async void HttpThread(object obj)
        {
            // find specific endpoint
			#if DEBUG
			string address = "localhost";
			#else
			string address;
			if (Environment.OSVersion.Platform == PlatformID.Win32NT) address = "+";
			else address = "0.0.0.0";
			/*foreach (var i in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (i.OperationalStatus == OperationalStatus.Up && !i.IsReceiveOnly)
				{
					var p = i.GetIPProperties();
					if (p.GatewayAddresses.Count == 0) continue;// skip virtual connections
					foreach (var a in p.UnicastAddresses)
					{
						Console.WriteLine("Found address " + a.Address.ToString());
						var ipAddress = a.Address;
						if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6) continue;
						if (ipAddress.Equals(IPAddress.Loopback) || ipAddress.Equals(IPAddress.IPv6Loopback)) continue;
						address = a.Address.ToString();
						break;
					}
				}
			}*/
			#endif

			// open NAT for port
			//natDevice = NATUtils.OpenPort(true, true, 8085, 60 * 60 * 8, "VideoShare");

			// format URL
			address = $"http://{address}:8085/";

			// create and start the HTTP listener
			Console.WriteLine("Listening on " + address);
			httpListener = new HttpListener();
			httpListener.Prefixes.Add(address);
			httpListener.Start();

			// handle requests
			while (httpThreadAlive)
			{
				try
				{
					// get the incoming request
					var context = await httpListener.GetContextAsync();
					if (context == null || context.Request == null) continue;

					ProcessRequest(context);
				}
				catch (Exception e)
				{
					Console.WriteLine(e.Message);
				}
			}
        }

		private static async void ProcessRequest(HttpListenerContext context)
		{
			try
			{
				HttpListenerRequest request = context.Request;
				HttpListenerResponse response = context.Response;
				response.StatusCode = (int)HttpStatusCode.OK;// default ok

				// check query string
				var requestType = RequestType.Normal;
				string requestQuePath = null;
				foreach (var queryKey in request.QueryString.AllKeys)
				{
					var queryValue = request.QueryString[queryKey];
					if (queryKey == "files")
					{
						requestType = RequestType.RequestFiles;
						requestQuePath = queryValue;
						break;
					}
					else if (queryKey == "download")
					{
						requestType = RequestType.DownloadFile;
						requestQuePath = queryValue;
						break;
					}
				}

				// serve files to client
				try
				{
					if (requestType == RequestType.Normal)
					{
						string urlPath = request.Url.AbsolutePath;
						if (urlPath == "/") urlPath = "index.htm";
						else urlPath = urlPath.Substring(1);
						#if DEBUG && ALLOW_DEBUG_PATH
						string path = Path.Combine(Environment.CurrentDirectory, @"..\..\..\", "Http", urlPath).Replace('\\', Path.DirectorySeparatorChar);
						path = Path.GetFullPath(path);
						#else
						string path = Path.Combine(Environment.CurrentDirectory, "Http", urlPath);
						#endif

						Console.WriteLine($"Requested file: '{path}'");
						using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
						{
							// Set the response headers
							if (request.Url.IsFile) response.ContentType = request.ContentType;
							else response.ContentType = "text/html";
							response.ContentLength64 = fileStream.Length;

							// Copy the file stream to the response output stream
							await fileStream.CopyToAsync(response.OutputStream);
						}
					}
					else if (requestType == RequestType.RequestFiles)
					{
						Console.WriteLine($"Requested files from: '{requestQuePath}'");

						// detect if we went back home
						if (requestQuePath != "$root$")
						{
							bool isValidSharePath = false;
							foreach (string sharePath in sharePaths)
							{
								if (requestQuePath.StartsWith(sharePath))
								{
									isValidSharePath = true;
									if (sharePath.Length > requestQuePath.Length)
									{
										requestQuePath = "$root$";
										break;
									}
								}
							}

							if (!isValidSharePath) requestQuePath = "$root$";
						}

						// grab files
						string[] files;
						if (requestQuePath == "$root$")
						{
							files = new string[sharePaths.Count + 1];
							files[0] = "";// root is blank here
							for (int i = 0; i != sharePaths.Count; ++i) files[i + 1] = sharePaths[i];
						}
						else
						{
							var folderValues = Directory.GetDirectories(requestQuePath);
							var fileValues = Directory.GetFiles(requestQuePath);
							files = new string[folderValues.Length + fileValues.Length + 1];
							files[0] = requestQuePath;// folder file path
							for (int i = 0; i != folderValues.Length; ++i) files[i + 1] = folderValues[i];
							for (int i = 0; i != fileValues.Length; ++i) files[i + folderValues.Length + 1] = fileValues[i];
						}

						// send files
						using (var stream = new MemoryStream())
						using (var writer = new StreamWriter(stream, Encoding.UTF8))
						{
							// write file list to memory
							foreach (string file in files)
							{
								Console.WriteLine($"File found: '{file}'");
								writer.Write(file + "\n");
							}
							writer.Flush();
							stream.Flush();
							stream.Position = 0;

							// Set the response headers
							response.ContentType = "text/plain";
							response.ContentLength64 = stream.Length;
							response.AddHeader("Content-Disposition", "attachment; filename=list.txt");

							// Copy the file stream to the response output stream
							await stream.CopyToAsync(response.OutputStream);
						}
					}
					else if (requestType == RequestType.DownloadFile)
					{
						Console.WriteLine($"Requested to download or stream file: '{requestQuePath}'");

						// detect if we went back home
						bool isValidSharePath = false;
						foreach (string sharePath in sharePaths)
						{
							if (requestQuePath.StartsWith(sharePath))
							{
								isValidSharePath = true;
								break;
							}
						}

						if (!isValidSharePath)
						{
							response.StatusCode = (int)HttpStatusCode.BadRequest;
							Console.WriteLine("Non-valid share file requested");
							return;
						}

						// serve file to client
						using (var fileStream = new FileStream(requestQuePath, FileMode.Open, FileAccess.Read, FileShare.Read))
						{
							// handle range requests
							string rangeHeader = request.Headers["Range"];
							if (!string.IsNullOrEmpty(rangeHeader))
							{
								// read range
								long totalLength = fileStream.Length;
								string[] range = rangeHeader.Replace("bytes=", "").Split('-');
								if (range.Length == 0)
								{
									response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
									Console.WriteLine($"No values in range request");
									return;
								}
								long start = long.Parse(range[0]);
								long end = (range.Length >= 2 && !string.IsNullOrEmpty(range[1])) ? long.Parse(range[1]) : (totalLength - 1);

								Console.WriteLine($"Range requested startIndex:{start} endIndex:{end} fileSize:{totalLength}");
								if (start >= totalLength || end >= totalLength || start >= end)
								{
									Console.WriteLine($"Invalid range request");
									response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
									response.Headers.Add("Content-Range", $"bytes */{totalLength}");
									return;
								}

								// Set the response headers
								response.StatusCode = (int)HttpStatusCode.PartialContent;
								response.ContentLength64 = end - start;
								response.Headers.Add("Content-Range", $"bytes {start}-{end}/{totalLength}");
								response.Headers.Add("Content-Length", totalLength.ToString());
								response.Headers.Add("Accept-Ranges", "bytes");
								response.AddHeader("Content-Disposition", "attachment; filename=" + Path.GetFileName(requestQuePath));

								// Copy the file stream to the response output stream
								var buffer = new byte[1024 * 1024 * 8];// 8mb buffer is ideal for USB3 or faster devices
								fileStream.Seek(start, SeekOrigin.Begin);
								long read = start, endRead = end + 1;
								do
								{
									int size = fileStream.Read(buffer, 0, (int)Math.Min(buffer.LongLength, end - read));// blast read here instead of async read to avoid lag
									if (size <= 0) break;
									read += size;
									await response.OutputStream.WriteAsync(buffer, 0, size);
								} while (read < endRead);
							}
							else// normal download request
							{
								Console.WriteLine("Full download...");

								// Set the response headers
								response.ContentType = "application/octet-stream";
								response.ContentLength64 = fileStream.Length;
								response.AddHeader("Content-Disposition", "attachment; filename=" + Path.GetFileName(requestQuePath));

								// Copy the file stream to the response output stream
								await fileStream.CopyToAsync(response.OutputStream);
								Console.WriteLine("Download complete!");
							}
						}
					}
				}
				catch (Exception e2)
				{
					// Handle any errors
					response.StatusCode = (int)HttpStatusCode.InternalServerError;
					byte[] errorBytes = Encoding.UTF8.GetBytes("Error: " + e2.Message);
					response.OutputStream.Write(errorBytes, 0, errorBytes.Length);
					Console.WriteLine(e2);
				}
				finally
				{
					// always close the output stream
					response.OutputStream.Close();
					response.Close();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}
    }
}
