using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace SmartDev.MultiCurrencyTester.Connect.Tests
{
	[TestFixture]
	public class ConnectAPITests
	{
		private const string TestVariableName0 = "TestVariable0";
		private const string TestVariableName1 = "TestVariable1";
		private const string TestVariableName2 = "TestVariable2";
		private static int Timeout = 1000000;

		#region AsyncHelpers

		private class AsyncOperation : IDisposable
		{
			private readonly IAsyncResult _asyncResult;

			public AsyncOperation(IAsyncResult asyncResult)
			{
				_asyncResult = asyncResult;
			}

			public void Dispose()
			{
				Assert.That(_asyncResult.AsyncWaitHandle.WaitOne(Timeout), Is.True);
			}
		}

		private AsyncOperation RunAsync(Action action)
		{
			return new AsyncOperation(action.BeginInvoke(null, null));
		}

		private void WaitForStartInitialization(ConnectImpl conn)
		{
			int i = Timeout;
			while (conn.InstanceStatus == InstanceStatus.NotInitialized && i > 0)
			{
				Thread.Sleep(100);
				i -= 100;
			}
			Assert.That(conn.InstanceStatus, Is.Not.EqualTo(InstanceStatus.NotInitialized));
		}

		private void WaitForStatusWaiting(ConnectImpl conn)
		{
			int i = Timeout;
			while (!conn.IsInstanceWaiting)
			{
				Thread.Sleep(100);
				i -= 100;
			}
			Assert.That(conn.IsInstanceWaiting, Is.True);
		}

		private void WaitForStatus(ConnectImpl conn, InstanceStatus staus)
		{
			int i = Timeout;
			while (conn.InstanceStatus != staus && i > 0)
			{
				Thread.Sleep(100);
				i -= 100;
			}
			Assert.That(conn.InstanceStatus, Is.EqualTo(staus));
		}

		#endregion

		[Test]
		public void InitializeTestAPI_ThrowExIfInstancesCountLessThanOne()
		{
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(0, 0));
			}
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(0, -5));
			}
		}

		[Test]
		public void InitializeTestAPI_ThrowExIfInstanceIdLessThenZero()
		{
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(-2, 2));
			}
		}

		[Test]
		public void InitializeTestAPI_ThrowExIfInstanceIdGreaterThanInstancesCount()
		{
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(2, 2));
			}
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(8, 5));
			}
		}

		[Test]
		public void InitializeTestAPI_ThrowExWhenReceiveDifferentInstancesCount()
		{
			using (var conn = new ConnectImpl())
			{
				conn.InitializeTestAPI(0, 1);
				Assert.Throws<ArgumentException>(() => conn.InitializeTestAPI(1, 5));
			}
		}

		[Test]
		public void InitializeTestAPI_ShouldWaitAllInstances()
		{
			const int instancesCount = 4;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			using (var conn2 = new ConnectImpl())
			using (var conn3 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				using (RunAsync(() => conn2.InitializeTestAPI(2, instancesCount)))
				{
					WaitForStatusWaiting(conn0);
					Assert.That(conn0.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					WaitForStatusWaiting(conn1);
					Assert.That(conn1.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					WaitForStatusWaiting(conn2);
					Assert.That(conn2.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					using (RunAsync(() => conn3.InitializeTestAPI(3, instancesCount)))
					{
						WaitForStartInitialization(conn3);
						Assert.That(conn3.IsInstanceWaiting, Is.False);
					}
				}
				Assert.That(conn0.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn1.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn2.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn3.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
			}
		}

		[Test]
		public void DeinitializeTestAPI_ThrowExIfIsntInitialized()
		{
			using (var conn = new ConnectImpl())
			{
				Assert.Throws<InvalidOperationException>(conn.DeinitializeTestAPI);
			}
		}

		[Test]
		public void InitializeTestAPI_DoNotWaitDeinitializedInstances()
		{
			const int instancesCount = 4;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			using (var conn2 = new ConnectImpl())
			using (var conn3 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				using (RunAsync(() => conn2.InitializeTestAPI(2, instancesCount)))
				{
					WaitForStatusWaiting(conn0);
					Assert.That(conn0.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					WaitForStatusWaiting(conn1);
					Assert.That(conn1.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					WaitForStatusWaiting(conn2);
					Assert.That(conn2.InstanceStatus, Is.Not.EqualTo(InstanceStatus.Initialized));

					using (RunAsync(() =>
						{
							conn3.InitializeTestAPI(3, instancesCount);
							conn3.DeinitializeTestAPI();
						}))
					{
						WaitForStartInitialization(conn3);
						Assert.That(conn3.IsInstanceWaiting, Is.False);
					}
				}
				Assert.That(conn0.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn1.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn2.InstanceStatus, Is.EqualTo(InstanceStatus.Initialized));
				Assert.That(conn3.InstanceStatus, Is.EqualTo(InstanceStatus.Deinitialized));
			}
		}

		//[Test]
		//public void GetLastInstanceTick_ShouldReturnsLastTick()
		//{
		//    using (var conn = new ConnectImpl())
		//    {
		//        conn.InitializeTestAPI(0, 1);
		//        conn.NextTick(100, 0, 0);
		//        Assert.That(conn.InstanceTick, Is.EqualTo(100));
		//        conn.NextTick(101, 0, 0);
		//        Assert.That(conn.InstanceTick, Is.EqualTo(101));
		//    }
		//}

		[Test]
		public void GetVariable_IfVariableDoesntExistReturn0()
		{
			using (var conn = new ConnectImpl())
			{
				conn.InitializeTestAPI(0, 1);
				Assert.That(conn.GetVariable(TestVariableName0), Is.EqualTo(0));
			}
		}

		[Test]
		public void GetVariable_ReturnLastUpdatedVariable()
		{
			using (var conn = new ConnectImpl())
			{
				conn.InitializeTestAPI(0, 1);
				conn.NextTick(100, 0, 0);
				conn.SetVariable(TestVariableName0, 10);
				Assert.That(conn.GetVariable(TestVariableName0), Is.EqualTo(10));
				conn.NextTick(101, 0, 0);
				Assert.That(conn.GetVariable(TestVariableName0), Is.EqualTo(10));
				conn.SetVariable(TestVariableName0, 11);
				Assert.That(conn.GetVariable(TestVariableName0), Is.EqualTo(11));
			}
		}

		//[Test]
		//public void GetVariable_ReturnLastUpdatedVariableForEachInstance()
		//{
		//    const int instancesCount = 3;

		//    using (var conn0 = new ConnectImpl())
		//    using (var conn1 = new ConnectImpl())
		//    using (var conn2 = new ConnectImpl())
		//    {
		//        using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
		//        using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
		//        using (RunAsync(() => conn2.InitializeTestAPI(2, instancesCount)))
		//        {
		//        }
		//        conn0.NextTick(100, 0, 0);
		//        conn0.SetVariable(TestVariableName0, 10);

		//        //conn0.GetLastTick() 100
		//        //conn1.GetLastTick() 0
		//        //conn2.GetLastTick() 0
		//        //conn1 willn't wait any and get empty
		//        Assert.That(conn1.GetVariable(TestVariableName0), Is.EqualTo(0));


		//        //conn0.GetLastTick() 100
		//        //conn1.GetLastTick() 101
		//        //conn2.GetLastTick() 0
		//        //conn1 willn't wait any and get his value 11
		//        conn1.NextTick(101, 0, 0);
		//        conn1.SetVariable(TestVariableName0, 11);
		//        Assert.That(conn1.GetVariable(TestVariableName0), Is.EqualTo(11));

		//        //conn0.GetLastTick() 100
		//        //conn1.GetLastTick() 101
		//        //conn2.GetLastTick() 101
		//        //conn0 is last and get old value 10
		//        //conn1 have set value this tick so can receive without waiting - 12
		//        //conn2 willn't wait any and get his value 12
		//        conn2.NextTick(101, 0, 0);
		//        conn2.SetVariable(TestVariableName0, 12);
		//        Assert.That(conn0.GetVariable(TestVariableName0), Is.EqualTo(10));
		//        Assert.That(conn1.GetVariable(TestVariableName0), Is.EqualTo(12));
		//        Assert.That(conn2.GetVariable(TestVariableName0), Is.EqualTo(12));


		//        //conn0.GetLastTick() 100
		//        //conn1.GetLastTick() 102
		//        //conn2.GetLastTick() 102
		//        //conn2 can get value that was set in this tick
		//        conn1.NextTick(102, 0, 0);
		//        conn2.NextTick(102, 0, 0);
		//        conn1.SetVariable(TestVariableName0, 13);
		//        Assert.That(conn2.GetVariable(TestVariableName0), Is.EqualTo(13));
		//    }
		//}

		[Test]
		public void GetVariable_ShouldWaitLastChanges()
		{
			const int instancesCount = 2;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				{
				}

				//conn0.GetLastTick() 100
				//conn1.GetLastTick() 0
				//conn0 should wait conn1
				conn0.NextTick(100, 0, 0);
				using (RunAsync(() => conn0.GetVariable(TestVariableName0)))
				{
					WaitForStatusWaiting(conn0);
					conn1.NextTick(101, 0, 0);
				}

				//conn0.GetLastTick() 102
				//conn1.GetLastTick() 101
				//conn0 should wait conn1
				conn0.NextTick(102, 0, 0);
				using (RunAsync(() => conn0.GetVariable(TestVariableName0)))
				{
					WaitForStatusWaiting(conn0);
					conn1.NextTick(102, 0, 0);
					conn0.SetVariable(TestVariableName0, 10);
				}

				//conn0.GetLastTick() 103
				//conn1.GetLastTick() 102
				//conn0 should wait conn1
				conn0.NextTick(103, 0, 0);
				using (RunAsync(() => conn0.SetVariable(TestVariableName0, 11)))
				{
					WaitForStatusWaiting(conn0);
					conn1.NextTick(103, 0, 0);
				}
			}
		}

		[Test]
		public void GetVariable_ShouldNotWaitDeinitializatedInstances()
		{
			const int instancesCount = 2;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				{
				}

				conn0.NextTick(100, 0, 0);
				using (RunAsync(() => conn0.GetVariable(TestVariableName0)))
				{
					//conn0.GetLastTick() 100
					//conn1.GetLastTick() 0
					//conn0 should wait conn1
					WaitForStatusWaiting(conn0);

					//conn1 deinitializated
					//conn0 shouldn't wait
					conn1.DeinitializeTestAPI();
					WaitForStatus(conn0, InstanceStatus.Initialized);
				}
			}
		}

		[Test]
		public void GetVariable_ShouldNotWaitDeinitializatedHimself()
		{
			const int instancesCount = 2;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				{
				}

				conn0.NextTick(100, 0, 0);
				using (RunAsync(() => conn0.GetVariable(TestVariableName0)))
				{
					//conn0.GetLastTick() 100
					//conn1.GetLastTick() 0
					//conn0 should wait conn1
					WaitForStatusWaiting(conn0);

					//conn0 deinitializated
					//conn0 getvalue shouldn't wait
					conn0.DeinitializeTestAPI();
					WaitForStatus(conn0, InstanceStatus.Deinitialized);
				}
			}
		}

		[Test, Ignore]
		public void GetVariable_CanSummValuesOfVariable()
		{
			const int instancesCount = 2;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				{
				}

				conn0.DeclareVariable(TestVariableName0, VariableOperations.Sum);

				conn0.NextTick(100, 0, 0);
				conn1.NextTick(100, 0, 0);
				conn0.SetVariable(TestVariableName0, 10);
				conn0.NextTick(101, 0, 0);
				conn1.NextTick(101, 0, 0);
				conn1.SetVariable(TestVariableName0, 12);
				conn0.NextTick(102, 0, 0);
				conn1.NextTick(102, 0, 0);

				Assert.That(conn0.GetVariable(TestVariableName0), Is.EqualTo(22));
				Assert.That(conn1.GetVariable(TestVariableName0), Is.EqualTo(22));
			}
		}

		[Test]
		public void GetVariable_ShouldNotWaitWhenAllInstancesIsHere()
		{
			const int instancesCount = 3;

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			using (var conn2 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				using (RunAsync(() => conn2.InitializeTestAPI(2, instancesCount)))
				{
				}

				conn0.NextTick(100, 0, 0);
				conn1.NextTick(100, 0, 0);
				conn2.NextTick(100, 0, 0);

				conn0.SetVariable(TestVariableName0, 10);

				conn0.NextTick(101, 0, 0);
				conn1.NextTick(101, 0, 0);

				using (RunAsync(() => conn1.GetVariable(TestVariableName0)))
				{
					WaitForStatusWaiting(conn1);
					conn2.NextTick(101, 0, 0);
				}
			}
		}

		[Test, Ignore]
		public void StressTest()
		{
			Timeout = 10000000;

			const int instancesCount = 10;
			const int cycleCount = 1000;
			var rnd = new Random();

			using (var conn0 = new ConnectImpl())
			using (var conn1 = new ConnectImpl())
			using (var conn2 = new ConnectImpl())
			using (var conn3 = new ConnectImpl())
			using (var conn4 = new ConnectImpl())
			using (var conn5 = new ConnectImpl())
			using (var conn6 = new ConnectImpl())
			using (var conn7 = new ConnectImpl())
			using (var conn8 = new ConnectImpl())
			using (var conn9 = new ConnectImpl())
			{
				using (RunAsync(() => conn0.InitializeTestAPI(0, instancesCount)))
				using (RunAsync(() => conn1.InitializeTestAPI(1, instancesCount)))
				using (RunAsync(() => conn2.InitializeTestAPI(2, instancesCount)))
				using (RunAsync(() => conn3.InitializeTestAPI(3, instancesCount)))
				using (RunAsync(() => conn4.InitializeTestAPI(4, instancesCount)))
				using (RunAsync(() => conn5.InitializeTestAPI(5, instancesCount)))
				using (RunAsync(() => conn6.InitializeTestAPI(6, instancesCount)))
				using (RunAsync(() => conn7.InitializeTestAPI(7, instancesCount)))
				using (RunAsync(() => conn8.InitializeTestAPI(8, instancesCount)))
				using (RunAsync(() => conn9.InitializeTestAPI(9, instancesCount)))
				{
				}

				Func<ConnectImpl, AsyncOperation> runTest = c => RunAsync(() =>
					{
						for (int i = 0; i < cycleCount; ++i)
						{
							c.NextTick(i * 1000, 0, 0);
							c.GetVariable(TestVariableName0);
							c.SetVariable(TestVariableName0, rnd.Next());
							c.GetVariable(TestVariableName1);
							c.SetVariable(TestVariableName1, rnd.Next());
							c.GetVariable(TestVariableName2);
							c.SetVariable(TestVariableName2, rnd.Next());
						}
					});

				var stopwatch = new Stopwatch();
				stopwatch.Start();

				using (runTest(conn0))
				using (runTest(conn1))
				using (runTest(conn2))
				using (runTest(conn3))
				using (runTest(conn4))
				using (runTest(conn5))
				using (runTest(conn6))
				using (runTest(conn7))
				using (runTest(conn8))
				using (runTest(conn9))
				{
				}

				stopwatch.Stop();
				Console.WriteLine(stopwatch.Elapsed);
			}
		}
	}
}
