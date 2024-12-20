#define ALLOW_DEBUG_PATH
#define USE_STREAM_THREADS

using Reign;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Timers;

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
		private static int port = 8085;

		private static OpenedNATDevice natDevice;
		private static bool openNAT;

		private static string password = string.Empty;
		private static string externalAddress = "0.0.0.0";

        static void Main(string[] args)
        {
			// get paths
			sharePaths = new List<string>();
			#if DEBUG && ALLOW_DEBUG_PATH
			password = "abc123";
			string path = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\", "TestVideos").Replace('\\', Path.DirectorySeparatorChar);
			path = Path.GetFullPath(path);
			sharePaths.Add(path);
			#else
			if (args != null && args.Length != 0)
			{
				foreach (var arg in args)
				{
					if (arg.StartsWith("--OpenNAT"))
					{
						openNAT = true;
					}
					else if (arg.StartsWith("--Port="))
					{
						var values = arg.Split('=');
						if (values.Length != 2)
						{
							Console.WriteLine("Invalid port override");
							continue;
						}

						if (int.TryParse(values[1], out int portOverride)) port = portOverride;
						else Console.WriteLine("Invalid port");
					}
					else if (arg.StartsWith("--Pass="))
					{
						var values = arg.Split('=');
						if (values.Length != 2)
						{
							Console.WriteLine("Invalid password override");
							continue;
						}

						password = values[1];
					}
					else
					{
						sharePaths.Add(arg);
					}
				}
			}
			#endif

			if (sharePaths.Count == 0)
			{
				Console.WriteLine("No share paths");
				return;
			}

			// hash password
			/*using (var sha256 = SHA256.Create())
			{
				byte[] bytes = Encoding.UTF8.GetBytes(password);
				byte[] hash = sha256.ComputeHash(bytes);
				var builder = new StringBuilder();
				foreach (byte b in hash)
				{
					builder.Append(b.ToString("x2")); // Convert each byte to a hex string
				}
				password = builder.ToString();
			}*/

			// start http thread
			Console.WriteLine("Type 'q' to exit");
			httpThreadAlive = true;
            httpThread = new Thread(HttpThread);
            httpThread.IsBackground = false;// let http thread dispose correctly
            httpThread.Start();

			// handle events
			while (httpThreadAlive)
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

			if (natDevice != null)
			{
				NATUtils.ClosePort(natDevice);
				Console.WriteLine("NAT closed");
			}
        }

        private static async void HttpThread(object obj)
        {
			// get external address
			GetExternalIPAddress();
			var externalRefreshTime = TimeSpan.FromHours(24);
			var timer = new System.Threading.Timer(GetExternalIPAddress_Timer, null, externalRefreshTime, externalRefreshTime);

            // find specific endpoint
			#if DEBUG
			string address = "localhost";
			#else
			string address = "+";
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
			if (openNAT)
			{
				try
				{
					natDevice = NATUtils.OpenPort(true, true, port, 60 * 60 * 8, "VLCVideoShare");
				}
				catch (Exception e)
				{
					Console.WriteLine("Failed to open NAT" + e.ToString());
				}
			}

			// format URL
			address = $"http://{address}:{port}/";

			// create and start the HTTP listener
			Console.WriteLine("Listening on " + address);
			try
			{
				httpListener = new HttpListener();
				httpListener.Prefixes.Add(address);
				httpListener.Start();
			}
			catch (Exception e)
			{
				Console.WriteLine("Failed to start HTTP listener" + e.ToString());
				goto SHUTDOWN;
			}

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

			// shutdown
			SHUTDOWN:;
			timer.Dispose();
			httpThreadAlive = false;
        }

        private static async void ProcessRequest(HttpListenerContext context)
		{
			try
			{
				HttpListenerRequest request = context.Request;
				HttpListenerResponse response = context.Response;
				response.StatusCode = (int)HttpStatusCode.OK;// default ok

				// check query string
				string requestPass = string.Empty;
				var requestType = RequestType.Normal;
				string requestQuePath = null;
				foreach (var queryKey in request.QueryString.AllKeys)
				{
					var queryValue = request.QueryString[queryKey];
					if (queryKey == "files")
					{
						requestType = RequestType.RequestFiles;
						requestQuePath = queryValue;
					}
					else if (queryKey == "download")
					{
						requestType = RequestType.DownloadFile;
						requestQuePath = queryValue;
					}
					else if (queryKey == "pass")
					{
						requestPass = queryValue.Trim();
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
						string[] folders, files = null;
						const int metaDataCount = 2;
						if (requestQuePath == "$root$")
						{
							folders = new string[sharePaths.Count + metaDataCount];
							lock (httpThread) folders[0] = externalAddress;
							folders[1] = string.Empty;// root is blank here
							for (int i = 0; i != sharePaths.Count; ++i) folders[i + metaDataCount] = sharePaths[i];
						}
						else
						{
							var folderValues = Directory.GetDirectories(requestQuePath);
							Array.Sort(folderValues);
							folders = new string[folderValues.Length + metaDataCount];
							lock (httpThread) folders[0] = externalAddress;
							folders[1] = requestQuePath;// metadata: folder file path
							for (int i = 0; i != folderValues.Length; ++i) folders[i + metaDataCount] = folderValues[i];

							files = Directory.GetFiles(requestQuePath);
							Array.Sort(files);
						}

						// send files
						using (var stream = new MemoryStream())
						using (var writer = new StreamWriter(stream, Encoding.UTF8))
						{
							// write folder list to memory
							int count = 0;
							foreach (string folder in folders)
							{
								if (string.IsNullOrEmpty(folder))
								{
									writer.Write('\n');
								}
								else if (count < metaDataCount)
								{
									writer.Write($"{folder}\n");
								}
								else if (requestPass == password)
								{
									var info = new DirectoryInfo(folder);
									if ((info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
									writer.Write($"{folder}^folder\n");
								}
								count++;
							}

							// write file list to memory
							if (files != null && requestPass == password)
							{
								foreach (string file in files)
								{
									var info = new FileInfo(file);
									if ((info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
									long size = info.Length;

									double kb = Math.Round(size / 1024.0, 2);
									double mb = Math.Round(size / 1024.0 / 1024.0, 2);
									double gb = Math.Round(size / 1024.0 / 1024.0 / 1024.0, 2);
									string sizeInfo;
									if (kb < 1024) sizeInfo = $"{kb} KB";
									else if (mb < 1024) sizeInfo = $"{mb} MB";
									else sizeInfo = $"{gb} GB";

									writer.Write($"{file}^file^{sizeInfo}\n");
								}
							}

							writer.Flush();
							stream.Flush();
							stream.Position = 0;

							// Set the response headers
							response.ContentType = "text/plain";
							response.ContentLength64 = stream.Length;
							response.AddHeader("Content-Disposition", "attachment; filename=\"list.txt\"");

							// Copy the file stream to the response output stream
							await stream.CopyToAsync(response.OutputStream);
						}
					}
					else if (requestType == RequestType.DownloadFile)
					{
						Console.WriteLine($"Requested to download or stream file: '{requestQuePath}'");
						if (requestPass != password)
						{
							Console.WriteLine($"Invalid password {requestPass} != {password}");
							return;
						}

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
									response.AddHeader("Content-Range", $"bytes */{totalLength}");
									return;
								}

								// Set the response headers
								response.StatusCode = (int)HttpStatusCode.PartialContent;
								response.ContentType = "application/octet-stream";
								response.ContentLength64 = (end - start) + 1;
								response.AddHeader("Content-Range", $"bytes {start}-{end}/{totalLength}");
								response.AddHeader("Accept-Ranges", "bytes");
								string filename = Path.GetFileName(requestQuePath);
								response.AddHeader("Content-Disposition", $"attachment; filename=\"{filename}\"");

								// Copy the file stream to the response output stream
								const int bufferSize = 1024 * 1024 * 128;// 128mb
								fileStream.Seek(start, SeekOrigin.Begin);
								long read = start, endRead = end + 1;

								#if USE_STREAM_THREADS
								var lockObj = new object();

								int bufferSwap = 0;
								var bufferSizes = new int[2];
								var buffers = new byte[2][];
								buffers[0] = new byte[bufferSize];
								buffers[1] = new byte[bufferSize];

								bool readThreadAlive = false;
								void ReadThread(object obj)
								{
									try
									{
										var buffer = buffers[bufferSwap];
										int size = (int)Math.Min(buffer.LongLength, endRead - read);
										bufferSizes[bufferSwap] = fileStream.Read(buffer, 0, size);
										if (bufferSizes[bufferSwap] <= 0) throw new Exception("Read zero bytes");
										read += bufferSizes[bufferSwap];
										bufferSwap = 1 - bufferSwap;// swap buffer
									}
									catch (Exception e)
									{
										Console.WriteLine(e.Message);
									}
									readThreadAlive = false;
								}

								Thread readThread;
								void LoadNextChunk()
								{
									var buffer = buffers[bufferSwap];
									int size = (int)Math.Min(buffer.LongLength, endRead - read);
									Console.WriteLine($"Reading chunk: Offset:{read} Size:{size} '{filename}'");

									readThread = new Thread(ReadThread);
									readThread.IsBackground = true;
									readThread.Priority = ThreadPriority.Lowest;
									readThreadAlive = true;
									readThread.Start();
								}

								LoadNextChunk();// load first chunk
								long write = start;
								while (write < endRead)
								{
									// wait for data chunk
									while (readThreadAlive) await Task.Yield();

									// pre-load next chunk
									int bufferSwapWrite = 1 - bufferSwap;
									long lastRead = read;
									LoadNextChunk();

									// write last data chunk
									var buffer = buffers[bufferSwapWrite];
									int size = bufferSizes[bufferSwapWrite];
									Console.WriteLine($"Writing chunk: Offset:{lastRead} Size:{size} '{filename}'");
									await response.OutputStream.WriteAsync(buffer, 0, size);
									write += size;
								}
								#else
								var buffer = new byte[bufferSize];
								do
								{
									int size = fileStream.Read(buffer, 0, (int)Math.Min(buffer.LongLength, endRead - read));// blast read here instead of async read to avoid IO lag
									if (size <= 0) break;
									read += size;
									await response.OutputStream.WriteAsync(buffer, 0, size);
								} while (read < endRead);
								#endif

								Console.WriteLine("Stream finished");
							}
							else// normal download request
							{
								Console.WriteLine("Full download...");

								// Set the response headers
								response.ContentType = "application/octet-stream";
								response.ContentLength64 = fileStream.Length;
								response.AddHeader("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(requestQuePath)}\"");

								// Copy the file stream to the response output stream
								await fileStream.CopyToAsync(response.OutputStream);
								Console.WriteLine("Download complete!");
							}
						}
					}
				}
				catch (Exception e)
				{
					// Handle any errors
					response.StatusCode = (int)HttpStatusCode.InternalServerError;
					byte[] errorBytes = Encoding.UTF8.GetBytes("Error: " + e.Message);
					response.OutputStream.Write(errorBytes, 0, errorBytes.Length);
					Console.WriteLine(e);
				}
				finally
				{
					// always close the output stream
					response.Close();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private static void GetExternalIPAddress_Timer(object sender)
        {
            GetExternalIPAddress();
        }

		private static async void GetExternalIPAddress()
		{
			Console.WriteLine("Getting external address...");
			using (var httpClient = new HttpClient())
			{
				try
				{
					var response = await httpClient.GetAsync("https://api.ipify.org");
					response.EnsureSuccessStatusCode();
					string result = await response.Content.ReadAsStringAsync();
					lock (httpThread) externalAddress = result;
				}
				catch (Exception ex)
				{
					Console.WriteLine("Failed to get external address: " + ex.Message);
					lock (httpThread) externalAddress = "0.0.0.0";
				}
			}
			Console.WriteLine("External address: " + externalAddress);
		}
    }
}
