using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Mediator;

public interface ICommand<out TResult>
    : IRequest<TResult>
{
}
