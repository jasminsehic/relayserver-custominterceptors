using Autofac;
using Thinktecture.Relay.Server.Interceptor;

namespace Thinktecture.Relay.ForwardedMemcachedCustomCode
{
	/// <inheritdoc />
	/// <summary>
	/// A RelayServer custom code assembly may provide a single Autofac module, that will register all
	/// types that are implemented and should be used.
	/// </summary>
	public class CustomCodeModule : Module
	{
		/// <inheritdoc />
		/// <summary>
		/// Override the Load method of the Autofac module to register the types.
		/// </summary>
		/// <param name="builder"></param>
		protected override void Load(ContainerBuilder builder)
		{
			// Register Forwarded Header interceptor with the container builder as its interface type
			builder.RegisterType<ForwardedRequestInterceptor>().As<IOnPremiseRequestInterceptor>();

			// Override the post data temporary store with a custom Memcached version
			builder.RegisterType<MemcachedPostDataTemporaryStore>().AsImplementedInterfaces().SingleInstance();

			base.Load(builder);
		}
	}
}
