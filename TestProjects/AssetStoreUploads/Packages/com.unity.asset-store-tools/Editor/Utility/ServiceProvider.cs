using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Utility
{
    internal abstract class ServiceProvider<Service>
    {
        private Dictionary<Type, Service> _services = new Dictionary<Type, Service>();
        private Dictionary<Type, Func<Service>> _queuedServices = new Dictionary<Type, Func<Service>>();

        protected class MissingServiceDependencyException : Exception
        {
            public Type ServiceType { get; private set; }
            public Type MissingDependencyType { get; private set; }

            public MissingServiceDependencyException(Type serviceType, Type missingDependencyType)
            {
                ServiceType = serviceType;
                MissingDependencyType = missingDependencyType;
            }
        }

        protected ServiceProvider()
        {
            RegisterServices();
            CreateRegisteredServices();
        }

        protected abstract void RegisterServices();

        protected void Register<TService, TInstance>() where TService : Service where TInstance : TService
        {
            Register<TService>(() => CreateServiceInstance(typeof(TInstance)));
        }

        protected void Register<TService>(Func<Service> initializer) where TService : Service
        {
            _queuedServices.Add(typeof(TService), initializer);
        }

        private void CreateRegisteredServices()
        {
            if (_queuedServices.Count == 0)
                return;

            var createdAnyService = false;
            var missingServices = new List<MissingServiceDependencyException>();

            foreach (var service in _queuedServices)
            {
                try
                {
                    var instance = service.Value.Invoke();
                    _services.Add(service.Key, instance);
                    createdAnyService = true;
                }
                catch (MissingServiceDependencyException e)
                {
                    missingServices.Add(e);
                }
            }

            foreach (var createdService in _services)
            {
                _queuedServices.Remove(createdService.Key);
            }

            if (!createdAnyService)
            {
                var message = string.Join(", ", missingServices.Select(x => $"{x.ServiceType} depends on {x.MissingDependencyType}"));
                throw new Exception("Could not create the following services due to missing dependencies: " + message);
            }

            // Recursively register remaining queued services that may have failed
            // due to missing depenedencies that are now registered
            CreateRegisteredServices();
        }

        private Service CreateServiceInstance(Type concreteType)
        {
            if (concreteType.IsAbstract)
                throw new Exception($"Cannot create an instance of an abstract class {concreteType}");

            var constructor = concreteType.GetConstructors().First();
            var expectedParameters = constructor.GetParameters();
            var parametersToUse = new List<object>();

            foreach (var parameter in expectedParameters)
            {
                if (!_services.ContainsKey(parameter.ParameterType))
                    throw new MissingServiceDependencyException(concreteType, parameter.ParameterType);

                parametersToUse.Add(_services[parameter.ParameterType]);
            }

            return (Service)constructor.Invoke(parametersToUse.ToArray());
        }

        public T GetService<T>() where T : Service
        {
            return (T)GetService(typeof(T));
        }

        public object GetService(Type type)
        {
            if (!_services.ContainsKey(type))
                throw new Exception($"Service of type {type} is not registered");

            return _services[type];
        }
    }
}